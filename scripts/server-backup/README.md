# BiSheng Server 备份脚本

仓库内运维脚本，**不**内嵌于 Server 进程。完整说明见 [`src/docs/服务端备份与恢复运维指南.md`](../../src/docs/服务端备份与恢复运维指南.md)。

| 文件 | 用途 |
|------|------|
| `backup-bisheng.sh` | `sqlite3 .backup` + `uploads/` 打包 + 可选配置/DP 密钥 |
| `restore-bisheng.sh` | 停服后从备份目录恢复 |
| `crontab.example` | 每日定时备份示例 |
| `litestream.yml.example` | 可选 Litestream 连续复制（仅 DB；uploads 另备） |

依赖：`sqlite3`、`tar`、`bash`。

应用升级（停服替换程序文件）见同级目录 [`../server-upgrade/`](../server-upgrade/)。
