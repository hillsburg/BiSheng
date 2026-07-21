#!/usr/bin/env bash
# BiSheng Server 升级：备份 → 停服 → 校验 → 替换程序文件 → 启服
# 不会覆盖：bisheng.db*、uploads/、appsettings.Production.json、data-protection-keys/、backup/
# 用法见 scripts/server-upgrade/README.md
set -euo pipefail

BISHENG_HOME="${BISHENG_HOME:-/opt/bisheng}"
SERVICE_NAME="${SERVICE_NAME:-bisheng}"
HEALTH_URL="${HEALTH_URL:-http://127.0.0.1:8090/health}"
SKIP_BACKUP="${SKIP_BACKUP:-0}"
SKIP_HEALTH="${SKIP_HEALTH:-0}"
EXPECTED_SHA256="${EXPECTED_SHA256:-}"
DOWNLOAD_URL="${DOWNLOAD_URL:-}"
PACKAGE="${PACKAGE:-}"
BISHENG_USER="${BISHENG_USER:-bisheng}"
BISHENG_GROUP="${BISHENG_GROUP:-bisheng}"
HEALTH_RETRIES="${HEALTH_RETRIES:-30}"
HEALTH_INTERVAL_SEC="${HEALTH_INTERVAL_SEC:-2}"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
BACKUP_SCRIPT="${BACKUP_SCRIPT:-}"

STAMP="$(date +%Y%m%d-%H%M%S)"
WORK_ROOT="${BISHENG_HOME}/.upgrade-${STAMP}"
STAGE_DIR="${WORK_ROOT}/stage"
PRE_UPGRADE_TAR="${BISHENG_HOME}/pre-upgrade-${STAMP}.tar.gz"
PACKAGE_LOCAL=""
DID_STOP=0
REPLACED=0
EXIT_CODE=0

log() {
  echo "[upgrade-bisheng] $*"
}

die() {
  echo "[upgrade-bisheng] 错误: $*" >&2
  EXIT_CODE=1
  exit 1
}

cleanup_work() {
  if [[ -d "${WORK_ROOT}" ]]; then
    rm -rf "${WORK_ROOT}"
  fi
}

resolve_backup_script() {
  if [[ -n "${BACKUP_SCRIPT}" && -x "${BACKUP_SCRIPT}" ]]; then
    return 0
  fi

  if [[ -x /usr/local/bin/backup-bisheng.sh ]]; then
    BACKUP_SCRIPT=/usr/local/bin/backup-bisheng.sh
    return 0
  fi

  local sibling="${SCRIPT_DIR}/../server-backup/backup-bisheng.sh"
  if [[ -f "${sibling}" ]]; then
    BACKUP_SCRIPT="$(cd "$(dirname "${sibling}")" && pwd)/$(basename "${sibling}")"
    chmod +x "${BACKUP_SCRIPT}" 2>/dev/null || true
    return 0
  fi

  die "找不到 backup-bisheng.sh。请安装到 /usr/local/bin 或设置 BACKUP_SCRIPT="
}

is_preserved_name() {
  local name="$1"
  case "${name}" in
    bisheng.db|bisheng.db-wal|bisheng.db-shm|bisheng.db-journal) return 0 ;;
    uploads|backup|data-protection-keys) return 0 ;;
    appsettings.Production.json) return 0 ;;
    .upgrade-*|pre-upgrade-*.tar.gz|pre-restore-*) return 0 ;;
    .|..) return 0 ;;
  esac
  return 1
}

sha256_of() {
  local file="$1"
  if command -v sha256sum >/dev/null 2>&1; then
    sha256sum "${file}" | awk '{print $1}'
  elif command -v shasum >/dev/null 2>&1; then
    shasum -a 256 "${file}" | awk '{print $1}'
  else
    die "需要 sha256sum 或 shasum"
  fi
}

verify_sha256() {
  local file="$1"
  local expected="$2"
  local actual
  actual="$(sha256_of "${file}")"
  expected="$(echo "${expected}" | tr '[:upper:]' '[:lower:]' | tr -d '[:space:]')"
  actual="$(echo "${actual}" | tr '[:upper:]' '[:lower:]')"
  if [[ "${actual}" != "${expected}" ]]; then
    die "SHA-256 不匹配: expected=${expected} actual=${actual}"
  fi
  log "SHA-256 校验通过"
}

rollback_program() {
  if [[ "${REPLACED}" -ne 1 ]]; then
    return 0
  fi

  if [[ ! -f "${PRE_UPGRADE_TAR}" ]]; then
    log "警告: 无可用于回滚的 pre-upgrade 归档: ${PRE_UPGRADE_TAR}"
    return 0
  fi

  log "启服/健康检查失败，正在从 ${PRE_UPGRADE_TAR} 回滚程序文件…"
  systemctl stop "${SERVICE_NAME}" 2>/dev/null || true

  local tmp_restore
  tmp_restore="$(mktemp -d "${BISHENG_HOME}/.rollback-XXXXXX")"
  tar -xzf "${PRE_UPGRADE_TAR}" -C "${tmp_restore}"

  # 仅回滚归档内的程序文件，不动保留名单
  local item base
  shopt -s nullglob
  for item in "${tmp_restore}"/*; do
    base="$(basename "${item}")"
    if is_preserved_name "${base}"; then
      continue
    fi
    rm -rf "${BISHENG_HOME:?}/${base}"
    mv "${item}" "${BISHENG_HOME}/${base}"
  done
  shopt -u nullglob

  rm -rf "${tmp_restore}"
  if id "${BISHENG_USER}" >/dev/null 2>&1; then
    chown -R "${BISHENG_USER}:${BISHENG_GROUP}" "${BISHENG_HOME}" || true
  fi

  systemctl start "${SERVICE_NAME}" || true
  log "已尝试回滚程序文件并重新启动 ${SERVICE_NAME}"
}

on_exit() {
  local code=$?
  if [[ "${EXIT_CODE}" -ne 0 ]]; then
    code="${EXIT_CODE}"
  fi
  if [[ "${code}" -ne 0 ]]; then
    rollback_program || true
    if [[ "${DID_STOP}" -eq 1 ]]; then
      systemctl start "${SERVICE_NAME}" 2>/dev/null || true
    fi
  fi
  cleanup_work || true
}

trap on_exit EXIT

usage() {
  cat <<'EOF'
用法:
  sudo BISHENG_HOME=/opt/bisheng EXPECTED_SHA256=<hex> \
    upgrade-bisheng.sh /path/to/BiSheng.Server-<ver>-linux-x64.zip

  sudo DOWNLOAD_URL=https://... EXPECTED_SHA256=<hex> upgrade-bisheng.sh

环境变量:
  BISHENG_HOME       安装根（默认 /opt/bisheng）
  PACKAGE            本地 zip（也可作第 1 个参数）
  DOWNLOAD_URL       远程 zip（与 PACKAGE 二选一）
  EXPECTED_SHA256    包哈希（强烈建议）
  SKIP_BACKUP=1      跳过备份（仅调试）
  SKIP_HEALTH=1      跳过健康检查
  SERVICE_NAME       systemd 单元名（默认 bisheng）
  HEALTH_URL         健康检查 URL（默认 http://127.0.0.1:8090/health）
  BACKUP_SCRIPT      backup-bisheng.sh 路径
EOF
}

# --- 参数 ---
if [[ "${1:-}" == "-h" || "${1:-}" == "--help" ]]; then
  usage
  exit 0
fi

if [[ -n "${1:-}" ]]; then
  PACKAGE="$1"
fi

if [[ -z "${PACKAGE}" && -z "${DOWNLOAD_URL}" ]]; then
  usage
  die "请提供 PACKAGE 路径或 DOWNLOAD_URL"
fi

if [[ "$(id -u)" -ne 0 ]]; then
  die "请使用 root 运行（需要 systemctl / 写入 ${BISHENG_HOME}）"
fi

command -v systemctl >/dev/null 2>&1 || die "需要 systemctl"
command -v unzip >/dev/null 2>&1 || die "需要 unzip（apt install unzip）"
command -v tar >/dev/null 2>&1 || die "需要 tar"
command -v curl >/dev/null 2>&1 || die "需要 curl（健康检查 / 可选下载）"

if [[ ! -d "${BISHENG_HOME}" ]]; then
  die "BISHENG_HOME 不存在: ${BISHENG_HOME}"
fi

if ! systemctl cat "${SERVICE_NAME}" >/dev/null 2>&1; then
  die "找不到 systemd 单元: ${SERVICE_NAME}"
fi

resolve_backup_script

mkdir -p "${WORK_ROOT}" "${STAGE_DIR}"

# --- 准备安装包 ---
if [[ -n "${DOWNLOAD_URL}" ]]; then
  PACKAGE_LOCAL="${WORK_ROOT}/package.zip"
  log "下载: ${DOWNLOAD_URL}"
  curl -fL --retry 3 -o "${PACKAGE_LOCAL}" "${DOWNLOAD_URL}"
else
  PACKAGE_LOCAL="$(readlink -f "${PACKAGE}" 2>/dev/null || realpath "${PACKAGE}" 2>/dev/null || echo "${PACKAGE}")"
  [[ -f "${PACKAGE_LOCAL}" ]] || die "安装包不存在: ${PACKAGE}"
fi

if [[ -n "${EXPECTED_SHA256}" ]]; then
  verify_sha256 "${PACKAGE_LOCAL}" "${EXPECTED_SHA256}"
else
  log "警告: 未设置 EXPECTED_SHA256，跳过哈希校验（不推荐生产使用）"
fi

# --- 备份 ---
if [[ "${SKIP_BACKUP}" == "1" ]]; then
  log "警告: SKIP_BACKUP=1，跳过升级前备份"
else
  log "升级前备份…"
  BISHENG_HOME="${BISHENG_HOME}" "${BACKUP_SCRIPT}"
fi

# --- 停服 ---
log "停止 ${SERVICE_NAME}…"
systemctl stop "${SERVICE_NAME}"
DID_STOP=1

# --- 解压 ---
log "解压到 ${STAGE_DIR}"
unzip -q "${PACKAGE_LOCAL}" -d "${STAGE_DIR}"

# 若 zip 内多一层 publish/ 目录则下沉一层
if [[ ! -f "${STAGE_DIR}/BiSheng.Server.dll" ]]; then
  mapfile -t nested < <(find "${STAGE_DIR}" -mindepth 2 -maxdepth 2 -name 'BiSheng.Server.dll' 2>/dev/null || true)
  if [[ "${#nested[@]}" -eq 1 ]]; then
    STAGE_DIR="$(dirname "${nested[0]}")"
    log "检测到嵌套目录，使用: ${STAGE_DIR}"
  elif [[ ! -f "${STAGE_DIR}/BiSheng.Server.dll" ]]; then
    die "安装包中未找到 BiSheng.Server.dll"
  fi
fi

# --- 程序快照（用于失败回滚）---
log "归档当前程序文件 → ${PRE_UPGRADE_TAR}"
(
  cd "${BISHENG_HOME}"
  # 排除数据与备份目录
  tar -czf "${PRE_UPGRADE_TAR}" \
    --exclude='./bisheng.db' \
    --exclude='./bisheng.db-wal' \
    --exclude='./bisheng.db-shm' \
    --exclude='./bisheng.db-journal' \
    --exclude='./uploads' \
    --exclude='./backup' \
    --exclude='./data-protection-keys' \
    --exclude='./appsettings.Production.json' \
    --exclude='./.upgrade-*' \
    --exclude='./pre-upgrade-*.tar.gz' \
    --exclude='./pre-restore-*' \
    .
)

# --- 同步程序文件 ---
log "同步程序文件到 ${BISHENG_HOME}（保留数据与 Production 配置）"
shopt -s nullglob dotglob
for item in "${STAGE_DIR}"/*; do
  base="$(basename "${item}")"
  if is_preserved_name "${base}"; then
    log "跳过保留项: ${base}"
    continue
  fi
  # 勿用安装包内的 Development 配置覆盖生产习惯：仍允许覆盖 appsettings.json（非 Production）
  rm -rf "${BISHENG_HOME:?}/${base}"
  cp -a "${item}" "${BISHENG_HOME}/${base}"
done
shopt -u nullglob dotglob
REPLACED=1

if id "${BISHENG_USER}" >/dev/null 2>&1; then
  chown -R "${BISHENG_USER}:${BISHENG_GROUP}" "${BISHENG_HOME}"
fi

# --- 启服 ---
log "启动 ${SERVICE_NAME}…"
systemctl start "${SERVICE_NAME}"
DID_STOP=0

if [[ "${SKIP_HEALTH}" == "1" ]]; then
  log "已跳过健康检查（SKIP_HEALTH=1）"
else
  log "健康检查: ${HEALTH_URL}"
  ok=0
  for ((i = 1; i <= HEALTH_RETRIES; i++)); do
    if curl -sf -o /dev/null "${HEALTH_URL}"; then
      ok=1
      break
    fi
    sleep "${HEALTH_INTERVAL_SEC}"
  done
  if [[ "${ok}" -ne 1 ]]; then
    die "健康检查失败（${HEALTH_RETRIES} 次）: ${HEALTH_URL}"
  fi
  log "健康检查通过"
fi

systemctl --no-pager --full status "${SERVICE_NAME}" || true
log "升级完成。数据备份见 ${BISHENG_HOME}/backup/；程序回滚包: ${PRE_UPGRADE_TAR}"
