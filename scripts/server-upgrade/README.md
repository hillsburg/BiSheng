# BiSheng Server 升级脚本

将 GitHub Release 中的 `BiSheng.Server-<version>-linux-x64.zip` 安全应用到 `/opt/bisheng`。

完整设计见 [`src/docs/客户端与服务端更新机制设计文档.md`](../../src/docs/客户端与服务端更新机制设计文档.md) §3。

## 安装

```bash
sudo apt install -y unzip curl sqlite3
sudo cp scripts/server-backup/backup-bisheng.sh /usr/local/bin/
sudo cp scripts/server-upgrade/upgrade-bisheng.sh /usr/local/bin/
sudo chmod +x /usr/local/bin/backup-bisheng.sh /usr/local/bin/upgrade-bisheng.sh
```

## 用法

```bash
# 从 Release 下载 zip 与 SHA256SUMS 后：
sudo BISHENG_HOME=/opt/bisheng EXPECTED_SHA256=<hex> \
  /usr/local/bin/upgrade-bisheng.sh /tmp/BiSheng.Server-0.1.0-linux-x64.zip

# 或直接下载：
sudo DOWNLOAD_URL='https://github.com/hillsburg/BiSheng/releases/download/v0.1.0/BiSheng.Server-0.1.0-linux-x64.zip' \
  EXPECTED_SHA256=<hex> \
  /usr/local/bin/upgrade-bisheng.sh
```

## 流程摘要

1. 调用 `backup-bisheng.sh`（可用 `SKIP_BACKUP=1` 跳过，生产不建议）
2. `systemctl stop bisheng`
3. 校验 SHA-256（若提供 `EXPECTED_SHA256`）
4. 解压到临时目录；归档当前程序文件为 `pre-upgrade-*.tar.gz`
5. 覆盖程序文件，**保留** `bisheng.db*`、`uploads/`、`appsettings.Production.json`、`data-protection-keys/`、`backup/`
6. `systemctl start` + 访问 `HEALTH_URL`（默认 `/health`）
7. 失败时尝试用 `pre-upgrade-*.tar.gz` 回滚程序文件

## 与发版产物

CI / `scripts/release/pack-all.ps1` 产出的 Server zip 即可作为本脚本输入。