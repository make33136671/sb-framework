# 私有 NPM 源 (Verdaccio)

saibo 自己的 NPM registry,托管私有 npm 包 + 透传 npmjs.org 公共包。
派生项目的前端(vben admin fork 等)publish 私有组件、install 依赖,都可以走这里。

## 一图链路

```
派生项目  ──https──▶  npm.saibo.club  ──▶  公网 走 CF Tunnel ──▶ Verdaccio (192.168.10.100:4873)
                                          │
                                          └──  内网 走 NPM 反代  ──▶ Verdaccio (saibo_net 容器名)
                                               (用户证书 LiteSSL)

Verdaccio ──cache miss──▶  registry.npmjs.org  ──▶  上游公共包透传(config.yaml 里 uplinks.npmjs)
```

## 访问入口

| 路径 | URL |
|------|-----|
| 公网 / 内网(推荐) | `https://npm.saibo.club` |
| Web UI(浏览/搜索) | `https://npm.saibo.club` |
| 后端直连(只在 NAS 内有意义)| `http://192.168.10.100:4873` |

## 首次创建账号(必做)

verdaccio 默认任何人可读公共包,但 **publish 必须先 `adduser` 注册账号**(`config.yaml` 里 `$authenticated` 才能 publish)。

```bash
npm adduser --registry https://npm.saibo.club
# 交互输入: username / password / email
# 成功后 token 写入 ~/.npmrc
```

第一个注册的用户会被写到 `/vol1/1000/DockerData/Verdaccio/conf/htpasswd`(host 上文件),后续登录验证查这个文件。可以 `npm adduser` 多次创建不同用户(团队成员各自一个账号)。

## 配置项目使用 Verdaccio

### 方案 A:全局 registry(影响所有项目)

```bash
npm config set registry https://npm.saibo.club
```

### 方案 B:项目级 .npmrc(只影响本项目,推荐 monorepo)

项目根目录加 `.npmrc`:

```
registry=https://npm.saibo.club
```

### 方案 C:scoped registry(@saibo/* 走私有源,其他走 npmjs)

```
@saibo:registry=https://npm.saibo.club
```

这种最干净:私有包统一加 `@saibo/` scope,公共包仍走 npmjs.org 不变。

## publish 私有包

cd 到你的 npm package 目录(里面有 package.json):

```bash
# 必须先 npm adduser / npm login 一次
npm publish --registry https://npm.saibo.club

# 如果 package.json 里 "name": "@saibo/xxx" 且 .npmrc 配了 scope,直接:
npm publish
```

更新版本(每次 publish 前):

```bash
npm version patch    # 1.0.0 → 1.0.1
# 或 npm version minor / major
npm publish
```

> ⚠️ NPM 跟 NuGet 一样**不允许覆盖同版本**,publish 前 bump 版本号是必须的。
> `--access public` 默认对 scoped 包 publish 为 private,公开可读要显式加 `--access public`。

## install / restore

派生项目 `.npmrc` 配好后,**正常 `npm install` 就行**:

```bash
npm install
# 或 pnpm install / yarn install — 都会读 .npmrc
```

`uplinks.npmjs` 配置后,verdaccio 上没有的公共包会**自动从 npmjs.org 上游拉一份缓存**,客户端透明感知。后续 install 走本地,内网快。

## config.yaml 当前配置

```yaml
storage: /verdaccio/storage
auth:
  htpasswd:
    file: /verdaccio/conf/htpasswd
uplinks:
  npmjs:
    url: https://registry.npmjs.org/
packages:
  '@*/*':                     # 任何 scoped 包(@xxx/yyy)
    access: $all              # 所有人可读(包括匿名)
    publish: $authenticated   # 必须登录才能 publish
    proxy: npmjs              # cache miss 走上游 npmjs
  '**':                       # 任何无 scope 包
    access: $all
    publish: $authenticated
    proxy: npmjs
logs: [{type: stdout, format: pretty, level: http}]
```

host 路径:`/vol1/1000/DockerData/Verdaccio/conf/config.yaml`。
改完不用重启 verdaccio,它会 watch 文件 hot-reload(部分配置需要重启)。

## 故障排查

### 401 Unauthorized
- 没 `npm adduser` / `npm login`,或 token 失效
- `cat ~/.npmrc` 看 `_authToken` 是否还在
- 重新 `npm login --registry https://npm.saibo.club`

### 404 Not Found(install 时)
- 包名错了
- uplinks 没拉到(npmjs.org 临时不可达,或网络问题)
- `docker logs verdaccio` 看 http 日志

### 403 publish 失败
- 同版本号已存在(NPM 拒绝同版本 republish)→ `npm version patch` 再来
- scope `@xxx` 不属于你登录的 user(默认 verdaccio 只允许 publish 跟当前 user 同名的 scope,改 config.yaml 可放宽)

### 502 Bad Gateway(走内网域名时)
- saibo_net 网络没连上 / verdaccio 容器没起 / NPM Forward 配错
- `docker network inspect saibo_net` 看容器是否仍接入
- `docker logs verdaccio` 看容器自己是否健康

## 维护信息

- 部署机:`192.168.10.100`(saibo NAS),容器名 `verdaccio`
- 镜像:`verdaccio/verdaccio:latest`(实测 6.6.0)
- 数据目录:
  - 包存储:`/vol1/1000/DockerData/Verdaccio/storage`
  - 配置 + htpasswd:`/vol1/1000/DockerData/Verdaccio/conf`
- 容器跑法:**`--user 0:0` root 模式**(verdaccio 默认 uid=10001 在挂载的 storage 目录做 chown 失败误报 config 错;root 模式绕过,verdaccio 会 warn 但功能正常)
- 重启拉起:NAS 开机后由 `/vol1/1000/DockerData/delay_start_docker.sh` 第二阶段自动 start(跟 baget 同阶段)
- 反代规则:`npm_master`(NPM) → `verdaccio:4873`(saibo_net user-defined bridge)
- 证书:用户上传到 NPM 的 LiteSSL TrustAsia 证书,跟 nuget.saibo.club 同期(到期 `2026-08-11`)
