# 私有 NuGet 源 (BaGet)

saibo 自己的 NuGet feed,托管 SB.Framework.* 包 + Mirror 透传 nuget.org 公共包。
派生项目 push framework 包、restore 依赖,都走这里。

## 一图链路

```
派生项目  ──https──▶  nuget.saibo.club  ──▶  公网 走 CF Tunnel ──▶ BaGet (192.168.10.100:5555)
                                          │
                                          └──  内网 走 NPM 反代  ──▶ BaGet (saibo_net 容器名)
                                               (用户证书 LiteSSL,有效期 ~ 2026-08-11)

BaGet ──cache miss──▶  api.nuget.org  ──▶  上游公共包透传(Mirror__Enabled=true)
```

## 访问入口

| 路径 | URL |
|------|-----|
| 公网 / 内网(推荐统一用这个) | `https://nuget.saibo.club` |
| Web UI(浏览/搜索) | `https://nuget.saibo.club` |
| v3 service index | `https://nuget.saibo.club/v3/index.json` |
| 后端直连(只在 NAS 内有意义) | `http://192.168.10.100:5555` |

## ApiKey

`f0e8a9bb404c0c0e96f02b01c5ee8296`

push 时通过 `--api-key` 参数传;GET / restore 不需要 key。

## 配置 NuGet.config(派生项目)

在派生项目根目录 `NuGet.config` 加一个 source(放在 nuget.org 之前,优先内网):

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="saibo-baget" value="https://nuget.saibo.club/v3/index.json" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
  </packageSources>
</configuration>
```

> **建议**:配 saibo-baget 后,NuGet client 会先查 BaGet。即便 BaGet 上没有的公共包,Mirror 模式会从 nuget.org 上游透传一份缓存,后续走本地速度更快。
> `nuget.org` source 仍保留作为兜底,以防内网或 BaGet 临时不可达。

## push framework 包(发包流程)

bump csproj `<Version>` 并构建出 nupkg 后:

```bash
cd framework/core/nupkgs

# 双变体一起推(防漏一边导致派生项目拉不到)
for pkg in SB.Framework.*.0.1.0-preview.<N>.nupkg \
           SB.Framework.*.NoLicense.0.1.0-preview.<N>.nupkg; do
  dotnet nuget push "$pkg" \
    --source https://nuget.saibo.club/v3/index.json \
    --api-key f0e8a9bb404c0c0e96f02b01c5ee8296 \
    --skip-duplicate
done
```

> `--skip-duplicate` 让重推幂等无害(同版本会被忽略而不是报错)。
> 2026-05-13 起 **GitLab Packages NuGet 源已废弃**(性能差),所有 saibo 包统一只 push 到 BaGet,**不再**双源同步。派生项目 NuGet.config 也只配 BaGet。

## restore

派生项目正常跑就行:

```bash
dotnet restore
```

NuGet client 按 `packageSources` 顺序查询:先 BaGet,然后 nuget.org。BaGet 上的包包括:
- saibo 自己的 SB.Framework.* / SB.Framework.*.NoLicense
- 通过 Mirror 缓存的 nuget.org 包(每次 cache miss 会自动从上游拉,客户端透明)

## Mirror 行为

`Mirror__Enabled=true` 容器环境变量配置后:
- 客户端请求一个 BaGet 上没有的包(比如 `Newtonsoft.Json`),BaGet 检测 cache miss
- BaGet 从 `https://api.nuget.org/v3/index.json` 上游拉包元数据 + nupkg 内容,落地到本地存储
- 后续请求走本地,不再上游
- 实测 `Newtonsoft.Json` 透传 84 个版本(从 3.5.8 → 13.0.5-beta1)

## 故障排查

### 404 找不到包
1. Web UI 搜一下包是否在 BaGet
2. 不在但 nuget.org 有 → 看 BaGet 是否能访问外网(NAS 内 docker exec baget curl api.nuget.org)
3. push 过了但拉不到 → 检查 `--source` URL 是否对(`/v3/index.json` 不能漏)

### 403 push 失败
- 检查 `--api-key` 是否正确
- 检查 BaGet 容器 env `ApiKey` 是否仍为该值(`docker inspect baget`)

### 502 Bad Gateway(只可能内网遇到)
- NPM `192.168.10.2` 反代到 baget 容器名出问题
- 看 `docker network inspect saibo_net` 容器是否仍接入
- 看 `docker logs baget` 容器是否还活着

### restore 慢
- 第一次 cache miss 时透传 nuget.org 会慢(2~5 秒)
- 后续 hit cache 走本地(~50ms)

## 维护信息

- 部署机:`192.168.10.100`(saibo NAS),容器名 `baget`
- 数据目录:`/vol1/1000/DockerData/BaGet`(FileSystem + SQLite,可整体备份)
- 重启拉起:NAS 开机后由 `/vol1/1000/DockerData/delay_start_docker.sh` 第二阶段自动 start
- 反代规则:`npm_master`(NPM) → `baget:80`(saibo_net user-defined bridge)
- 证书:用户上传到 NPM 的 LiteSSL TrustAsia 证书,到期 `2026-08-11`,过期前要续期 + 重新上传到 NPM
- 镜像:`loicsharma/baget:latest`(每次 docker pull 拿最新)
