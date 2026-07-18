#!/usr/bin/env bash
# BiSheng Server 一致性备份：SQLite .backup + uploads/
# 用法见 scripts/server-backup/README.md 与 src/docs/服务端备份与恢复运维指南.md
set -euo pipefail

BISHENG_HOME="${BISHENG_HOME:-/opt/bisheng}"
BACKUP_ROOT="${BACKUP_ROOT:-${BISHENG_HOME}/backup}"
DB_PATH="${DB_PATH:-${BISHENG_HOME}/bisheng.db}"
UPLOADS_DIR="${UPLOADS_DIR:-${BISHENG_HOME}/uploads}"
RETENTION_DAYS="${RETENTION_DAYS:-14}"
STAMP="$(date +%Y%m%d-%H%M%S)"
DEST="${BACKUP_ROOT}/${STAMP}"

mkdir -p "${DEST}"

if [[ ! -f "${DB_PATH}" ]]; then
  echo "错误: 找不到数据库 ${DB_PATH}" >&2
  exit 1
fi

if ! command -v sqlite3 >/dev/null 2>&1; then
  echo "错误: 需要 sqlite3（apt install sqlite3）。请勿对运行中的库使用普通 cp。" >&2
  exit 1
fi

# 在线一致快照（优于热 cp WAL 库）
sqlite3 "${DB_PATH}" <<SQL
.backup ${DEST}/bisheng.db
SQL

if [[ -d "${UPLOADS_DIR}" ]]; then
  tar -C "$(dirname "${UPLOADS_DIR}")" -czf "${DEST}/uploads.tar.gz" "$(basename "${UPLOADS_DIR}")"
else
  echo "警告: uploads 目录不存在: ${UPLOADS_DIR}" >&2
fi

# 可选：一并备份生产配置与 Data Protection 密钥（不含密钥则跳过）
if [[ -f "${BISHENG_HOME}/appsettings.Production.json" ]]; then
  cp -a "${BISHENG_HOME}/appsettings.Production.json" "${DEST}/"
fi

if [[ -d "${BISHENG_HOME}/data-protection-keys" ]]; then
  tar -C "${BISHENG_HOME}" -czf "${DEST}/data-protection-keys.tar.gz" data-protection-keys
fi

cat > "${DEST}/manifest.txt" <<EOF
stamp=${STAMP}
db=${DB_PATH}
created_utc=$(date -u +%Y-%m-%dT%H:%M:%SZ)
hostname=$(hostname)
EOF

# 清理过期备份目录（按目录名时间戳排序后按天数删除）
if [[ "${RETENTION_DAYS}" =~ ^[0-9]+$ ]] && [[ "${RETENTION_DAYS}" -gt 0 ]]; then
  find "${BACKUP_ROOT}" -mindepth 1 -maxdepth 1 -type d -mtime "+${RETENTION_DAYS}" -exec rm -rf {} +
fi

echo "备份完成: ${DEST}"
ls -lah "${DEST}"
