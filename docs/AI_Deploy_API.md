# 应用部署接口标准（统一上传 + 部署）

> 适用范围:SB.Framework 及所有派生项目(sb-apps-pm / nexus / lottery / wuxing 等)。
> 实现位置:`SB.Framework.WebApi/Controllers/AppController.cs`、`UploadController.cs`。

## 一、核心结论

- 部署相关接口**全部在 Admin 端**(`/admin/App/*`、`/admin/Upload/*`),认证 scheme = `JwtAdmin`。
  **API 端(`/api/*`)没有任何部署接口** —— 部署只能从后台发起。
- **最新标准 = 两步式统一接口**:`Upload(target)` 上传拿 `jobId` → `Deploy(jobId)` 执行部署。
  一个 `target` 参数覆盖所有目标(API 二进制 / 前端项目),不再按类型分接口。
- 旧接口 `UploadApi` / `UploadProject`(上传即部署,一步到位)**仅为向后兼容保留**,内部已改写为
  "走统一 DeployJob 逻辑"的薄封装,**新代码 / 新前端 / 新文档一律用统一接口,不要再用旧接口**。

## 二、为什么从「两个」变成「一个」

| | 旧方式(兼容保留) | 新标准 |
|---|---|---|
| 接口数 | 2 个,按目标类型分:`UploadApi`(API 包)、`UploadProject`(前端包) | 1 套:`Upload` + `Deploy`,靠 `target` 区分目标 |
| 步骤 | 一步:上传即部署 | 两步:上传(持久化拿 jobId)→ 部署(凭 jobId) |
| 失败重试 | 文件可能已丢,需重传 | zip 持久化到 `deploy_archive/`,**部署失败可凭 jobId 重试**,不必重传 |
| 回滚 | 无 | 有,基于 DeployJob 历史 |

拆成两步的目的:上传与部署解耦,部署失败时 zip 仍在,凭 `jobId` 直接重试;并为回滚提供完整的作业历史。

## 三、统一接口(推荐,新标准)

### 1. 小文件:直接上传 + 部署

```
POST /admin/App/Upload      Content-Type: multipart/form-data
  target  = "api" | 项目 key(如 "admin" / "h5",对应 DeployConfig.Projects[].key)
  file    = <xxx.zip>       仅支持 zip,单文件上限 200MB
→ 返回 { jobId, uploadId(=jobId,兼容字段), target, targetName, fileName, fileSize, status="Uploaded" }
  注意:此接口只上传不部署。

POST /admin/App/Deploy      Content-Type: multipart/form-data
  uploadId  = <上一步的 jobId>    (字段名 uploadId 为向后兼容,实际传 jobId)
  keepConfig = true              (默认 true:保留根目录 appsettings*.json 不被覆盖)
  clearCache = false             (默认 false:仅 API 部署有效,部署后清缓存)
→ 执行部署;API 包部署成功后进程会优雅重启
```

### 2. 大文件:分片上传 → 凭路径部署

分片接口在 `UploadController`(`/admin/Upload/*`):

```
GET  /admin/Upload/Config         拉上传配置(MaxFileSizeMB / ChunkThresholdMB / MaxConcurrent / AllowedExtensions)
POST /admin/Upload/ChunkInit      { fileName, fileSize, fileMd5 } → 秒传检测 / 断点续传 / 新建会话,返回 uploadId
POST /admin/Upload/ChunkUpload    { uploadId, chunkIndex, chunk } 单分片上限 20MB
POST /admin/Upload/ChunkMerge     { uploadId } → 合并为最终文件,返回 filePath
GET  /admin/Upload/ChunkProgress?uploadId=  查询进度(含已传分片列表)
```

合并拿到 `filePath` 后,先预检再部署:

```
GET  /admin/App/PreviewDeploy?target=&filePath=&fileName=
   → { valid, error, target, targetName, targetPath, requiredMarker, fileName, fileSize, entries }
     valid=false 时不要继续部署;requiredMarker 是该 target 必须包含的入口标志文件。

POST /admin/App/DeployApiByPath        部署 API(分片场景)
   { filePath, fileName, fileSize, keepConfig=true, clearCache=false }
POST /admin/App/DeployProjectByPath    部署前端项目(分片场景)
   { key, filePath, fileName, fileSize }

POST /admin/App/CancelUpload           用户在确认弹窗点取消:清掉合并出的 temp zip
   { filePath }
```

> 三条 `DeployXxxByPath` 内部都会 `CreateFromFile` 建一个 DeployJob,再走统一的 `DeployByJob`,
> 因此同样享受 jobId 重试、回滚、状态流转。

## 四、部署作业(DeployJob)状态机

```
Uploaded ──Deploy──▶ Deploying ──成功──▶ Deployed
                          └──失败──▶ Failed
Deployed ──被新部署/回滚顶替──▶ RolledBack
```

- 同一 `target` 新部署成功后,上一个活动的 `Deployed` 自动转 `RolledBack`,并被新 job 的 `PreviousJobId` 指向。
- 旧前端传 `status=Pending` 会自动映射为 `Uploaded`(兼容)。

## 五、查询与回滚

```
GET  /admin/App/DeployJobs?target=&status=&page=&pageSize=     部署作业列表(新名,推荐)
GET  /admin/App/UploadRecords?...                              同上(旧名,兼容)
GET  /admin/App/DeployRecords?target=&page=&pageSize=          已完成历史(Deployed+RolledBack,旧名兼容)
GET  /admin/App/DeployTargets                                  可部署的前端项目列表
GET  /admin/App/Info                                           当前 API 版本/信息
POST /admin/App/Restart                                        手动重启 API

POST /admin/App/Rollback   { id }
   回滚到指定 DeployJob(目标须为 Deployed 或 RolledBack)。
   复用该 job 的归档 zip 建一个新 job(RollbackOfJobId 指向原 job)并立即部署;
   成功后新 job → Deployed,当前活动 Deployed → RolledBack。
```

## 六、部署行为细节(实现要点)

- **API 包部署(target=api)用原子替换**:解压到同盘 staging 子目录,再逐文件 `File.Move(overwrite)`(Linux 下是
  `rename(2)` 原子换 inode),避开 .NET runtime mmap 加载与 in-place 覆盖的 race —— 跨版本同时换几十个 dll 也安全。
  部署完 `GracefulStop` 优雅重启,由 systemd / supervisord 拉起新进程。
- **`keepConfig=true`**:只保护**根目录**的 `appsettings.json` / `appsettings.*.json` 不被 zip 覆盖;子目录同名文件不受保护。
- **前端项目部署**:先清空 `project.Path`(跳过软链接 ReparsePoint),再解压覆盖。
- **zip 入口校验**:`InspectZip` 按 target 要求 zip 内必须含对应入口标志(`requiredMarker`),不合格直接 `Failed`,不动现网。
- 上传上限:统一/旧上传接口 200MB;分片单片 20MB;直传(`/admin/Upload/Direct`)2GB。

## 七、迁移指引(给旧前端 / 旧脚本)

| 旧用法 | 换成 |
|---|---|
| `POST /admin/App/UploadApi` (file, keepConfig) | `Upload(target="api", file)` → `Deploy(jobId, keepConfig)` |
| `POST /admin/App/UploadProject` (key, file) | `Upload(target=key, file)` → `Deploy(jobId)` |
| 读 `uploadId` 字段 | 改读 `jobId`(`uploadId` 仍返回,等于 `jobId`,后续会移除) |
| `GET UploadRecords` | `GET DeployJobs` |

> 旧接口当前**未打 `[Obsolete]`**,仅靠注释说明。若要把统一接口确立为唯一标准,建议给
> `UploadApi` / `UploadProject` 加 `[Obsolete("请使用 Upload + Deploy 统一接口")]`,并在下一个大版本移除。
