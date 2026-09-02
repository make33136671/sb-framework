# Nginx 上传大小限制与静态文件配置指南

> 适用范围：所有基于 `sb-docker-template` 的单容器部署项目（weixin / tgvault / agenthub 等）。
> 单容器内 nginx 与 .NET API 同容器运行，nginx 三端口（模板 8080/8081/8082，各项目可能不同）前置代理 API。

## 一、上传文件大小限制（全链路）

一次上传请求从浏览器到落盘，要穿过 5 层限制，**任何一层卡住都会失败**。排查"传不上去"时按此表逐层核对：

| # | 层级 | 配置位置 | 默认值 | 超限表现 |
|---|------|----------|--------|----------|
| 1 | **nginx 请求体上限** | `<项目>-docker/nginx/default.conf` 顶部 | **nginx 内置默认仅 1MB** | 413 Request Entity Too Large |
| 2 | Kestrel 请求体上限 | `SB.Framework.WebApi/Extensions/SbFrameworkExtensions.cs` | 500MB | 413/连接重置 |
| 3 | multipart 表单上限 | 同上（`MultipartBodyLengthLimit`） | 500MB | InvalidDataException |
| 4 | 接口特性 | `SB.Framework.WebApi/Controllers/UploadController.cs` | 直传 `[RequestSizeLimit(2GB)]`、分片 20MB | 413 |
| 5 | **应用层业务限制** | API 的 `appsettings.json` → `UploadConfig:MaxFileSizeMB` | 框架默认 2048；派生项目常配 100 | 业务错误"文件大小不能超过 N MB" |
| 6 | 前端校验 | admin `components/Upload/internal/useUploadConfig.ts` | 启动时从接口动态拉取 #5 的值 | 浏览器端直接提示，不发请求 |

**最常见的坑**：只改了 `appsettings.json` 的 `MaxFileSizeMB`，没改 nginx——nginx 默认只放行 1MB，大文件在 nginx 层就被 413 拦截，根本到不了 API。

### 修改步骤（派生项目改上传大小时照做）

**第 1 步：nginx**（`<项目>-docker/nginx/default.conf` 顶部，http 上下文，对三个端口全局生效）：

```nginx
# 业务限制 100MB → nginx 设 128m（留 20~30% multipart 编码余量）
client_max_body_size 128m;
```

> 为什么不是 100m？nginx 限制的是整个 multipart 请求体（文件内容 + boundary/表单头编码开销），
> 恰好 100MB 的文件会因几百字节开销触发 413。128m 只是传输通道上限，
> 真正的文件大小门槛仍是应用层 `MaxFileSizeMB`，安全性不变。
> 经验值：`client_max_body_size ≈ MaxFileSizeMB × 1.28`。

**第 2 步：应用层**（API 的 `appsettings.json`）：

```json
"UploadConfig": {
  "MaxFileSizeMB": 100,
  "ChunkThresholdMB": 100
}
```

`ChunkThresholdMB` 控制分片/直传分界：文件大于该值走分片上传（每片默认 1MB，不受本条限制影响）。
前端无需改动——`useUploadConfig.ts` 会通过上传配置接口自动拿到新值。

**第 3 步：重新部署**：

```bash
bash redeploy.sh
docker exec -it <容器名> nginx -t   # 语法检查
```

> 框架层（Kestrel 500MB、接口 2GB）一般不需要动；只有业务限制要超过 500MB 时才需同步上调框架配置。

## 二、静态文件目录映射（admin/user 访问 API 静态文件）

API 运行时产生的文件（上传附件、协议图片等）位于容器内 `/app/api/wwwroot/` 下。
**Docker 中禁止用 `ln -s` 软链**：容器重建即失效、Windows 构建上下文里的软链打进 Linux 镜像会断链。
正规做法是 nginx `alias` 直读磁盘（nginx 与 API 同容器，本来就能直接读）。

### nginx 配置（8090 用户端、8091 管理端两个 server 块都要加）

```nginx
# 普通上传文件：URL /storage/... → 物理目录 /app/api/wwwroot/storage/
location /storage/ {
    alias /app/api/wwwroot/storage/;
    expires 7d;
    add_header Cache-Control "public";
    access_log off;
}

# 协议图片等运行时生成文件：URL /uploads/... → /app/api/wwwroot/uploads/
location /uploads/ {
    alias /app/api/wwwroot/uploads/;
    expires 7d;
    add_header Cache-Control "public";
    access_log off;
}
```

路径对应关系（`alias` 末尾的 `/` 不能少）：

```
/storage/uploads/abc.png  →  /app/api/wwwroot/storage/uploads/abc.png
/uploads/protocols/x.png  →  /app/api/wwwroot/uploads/protocols/x.png
```

权限：API 以 root 运行，新建文件默认 644、目录 755，nginx 的 www-data 可读，无需额外授权。

### 卷持久化（必须挂卷，否则重建容器文件全丢）

`docker-compose.yml`：

```yaml
services:
  app:
    volumes:
      - ${STORAGE_DATA_PATH:-storage_data}:/app/api/wwwroot/storage
      - ${UPLOADS_DATA_PATH:-uploads_data}:/app/api/wwwroot/uploads

volumes:
  storage_data:
  uploads_data:
```

- 变量留空 → Docker 命名卷（推荐，首次挂载自动把镜像内置文件拷进卷）
- 填宿主机绝对路径 → 绑定挂载（便于备份，但 Docker **不会**复制镜像内容，需自行保证目录存在、权限 755）

`Dockerfile` 里预建挂载点，避免卷挂载到不存在的目录：

```dockerfile
RUN mkdir -p /app/api/wwwroot/storage /app/api/wwwroot/uploads && \
    chmod 755 /app/api/wwwroot/storage /app/api/wwwroot/uploads
```

### 旧容器文件迁移（首次挂卷前）

```bash
# 重建前备份容器可写层里的运行时文件
docker cp <旧容器>:/app/api/wwwroot/storage/. ./storage-backup/
docker cp <旧容器>:/app/api/wwwroot/uploads/. ./uploads-backup/
# 用绑定挂载的，把备份放回宿主机目录后再 redeploy
```

## 三、验证清单

```bash
docker exec -it <容器名> nginx -t                              # nginx 语法
docker exec -it <容器名> ls -l /app/api/wwwroot/uploads         # 卷挂载生效
curl -I http://<host>:<admin端口>/uploads/protocols/<图>.png    # 静态文件 200
```

- 上传 <100MB 文件 → 成功；上传 >100MB → 收到应用层提示"文件大小不能超过 100MB"（说明 nginx 已放行，业务层拦截，符合预期）
- 若返回 **413** → nginx 层未放行，检查 `client_max_body_size` 是否在配置文件顶部且已 `redeploy`

## 四、参考实现

- weixin 项目：`apps/weixin/weixin-docker/nginx/default.conf`（alias + 128m 完整配置）
- 框架模板：`sb/sb-docker-template/nginx/default.conf`（已含 `client_max_body_size 128m`，新项目自带）
- 应用层配置：`SB.Framework.Services/FileUploadService.cs`、`ChunkUploadService.cs`
- 配置定义：`SB.Framework.Core/AppSettings.cs` 的 `UploadConfig`（`MaxFileSizeMB` / `ChunkThresholdMB` / `UploadPath`）
