/* 账号页：添加、重命名、删除通行密钥。依赖 passkey-common.js；密码与验证码仅用于本次请求。 */
(() => {
    "use strict";
    const { toCreationOptions, serialize, request, isSupported, show } = window.BiShengPasskey;
    const management = document.getElementById("passkey-management");
    const form = document.getElementById("passkey-form");
    const nameInput = document.getElementById("passkey-name");
    const password = document.getElementById("passkey-password");
    const totp = document.getElementById("passkey-totp");
    const totpGroup = document.getElementById("passkey-totp-group");
    const add = document.getElementById("passkey-add");
    const addOther = document.getElementById("passkey-add-other");
    const list = document.getElementById("passkey-list");
    const message = document.getElementById("passkey-message");

    // 验证时间由服务端签发，换发 Cookie 时不会刷新，因此页面内无需更新。
    const verifiedAt = Number(management.dataset.verifiedAt || 0) * 1000;
    const verificationWindow = Number(management.dataset.verificationWindow || 0) * 1000;
    const development = management.dataset.development === "true";
    let busy = false;

    /** 按当前时间决定是否需要验证码，并同步输入框状态；返回是否需要。 */
    function syncTotpField() {
        const required = !development && Date.now() - verifiedAt > verificationWindow;
        totpGroup.hidden = !required;
        totp.required = required;
        return required;
    }

    /** 带上重新授权凭据执行请求，结束后无论成败都清空密码和验证码。 */
    async function withReauthentication(send) {
        if (!password.value || (syncTotpField() && !totp.value)) {
            throw new Error("请先在上方填写当前密码及需要的验证码。");
        }
        try {
            return await send({ password: password.value, totpCode: totp.value });
        } finally {
            password.value = "";
            totp.value = "";
        }
    }

    function setButtonsDisabled(disabled) {
        for (const button of management.querySelectorAll("button")) {
            button.disabled = disabled;
        }
    }

    function describe(error) {
        if (error.name === "NotAllowedError") {
            return "已取消或验证超时，尚未添加通行密钥，可以重新尝试。";
        }
        if (error.name === "InvalidStateError") {
            return "这把通行密钥已绑定，请使用其他设备或密码管理器。";
        }
        return error.message;
    }

    /** 串行执行操作：期间禁用全部按钮，避免重复发起仪式。 */
    async function perform(action) {
        if (busy) {
            return;
        }
        busy = true;
        setButtonsDisabled(true);
        try {
            await action();
        } catch (error) {
            show(message, describe(error), "danger");
        } finally {
            busy = false;
            setButtonsDisabled(false);
        }
    }

    async function register(useOtherDevice) {
        const name = nameInput.value.trim();
        const options = await withReauthentication(auth => request("/register/options", { ...auth, name, useOtherDevice }));
        show(message, useOtherDevice
            ? "请按浏览器提示使用手机或安全密钥完成验证。"
            : "请在系统弹窗中使用 Windows Hello、Touch ID 或设备 PIN 完成验证。");
        const credential = await navigator.credentials.create({ publicKey: toCreationOptions(options) });
        show(message, "设备验证完成，正在保存通行密钥…");
        await request("/register/complete", serialize(credential));
        nameInput.value = "";
        show(message, "通行密钥添加成功！下次可在登录页使用通行密钥直接登录。", "success");
        try {
            await refresh();
        } catch {
            show(message, "通行密钥已添加，但列表刷新失败，请刷新页面查看。", "success");
        }
    }

    async function rename(key) {
        const name = prompt("新的通行密钥名称", key.name)?.trim();
        if (!name || name === key.name) {
            show(message, "已取消重命名。");
            return;
        }
        await request("/" + key.id + "/rename", { name });
        await refresh();
        show(message, "通行密钥已重命名。", "success");
    }

    async function remove(key) {
        if (!confirm("删除“" + key.name + "”？其他后台会话也将退出。")) {
            show(message, "已取消删除。");
            return;
        }
        await withReauthentication(auth => request("/" + key.id + "/delete", auth));
        await refresh();
        show(message, "通行密钥已删除。", "success");
    }

    function actionButton(text, action) {
        const button = document.createElement("button");
        button.type = "button";
        button.className = "btn btn-secondary";
        button.textContent = text;
        button.disabled = busy;
        button.addEventListener("click", () => perform(action));
        return button;
    }

    async function refresh() {
        const keys = await request("");
        list.replaceChildren();
        if (!keys.length) {
            list.textContent = "尚未绑定通行密钥。";
            return;
        }
        for (const key of keys) {
            const row = document.createElement("div");
            const label = document.createElement("p");
            label.textContent = key.name + " · " + key.rpId + " · 最近使用：" +
                (key.lastUsedAtUtc ? new Date(key.lastUsedAtUtc).toLocaleString() : "尚未使用");
            row.append(label, actionButton("重命名", () => rename(key)), actionButton("删除", () => remove(key)));
            list.append(row);
        }
    }

    if (!isSupported()) {
        show(message, "当前浏览器或连接不支持通行密钥，请使用 HTTPS 和支持的浏览器。", "danger");
        add.disabled = true;
        return;
    }

    form.addEventListener("submit", event => {
        event.preventDefault();
        syncTotpField();
        if (form.reportValidity()) {
            // 未指定提交按钮（例如按 Enter）时，默认走当前设备。
            perform(() => register(event.submitter === addOther));
        }
    });
    syncTotpField();
    add.disabled = false;
    addOther.disabled = false;
    show(message, "填写名称和当前密码后选择添加方式；Windows 用户可选择“在此设备上添加”。");
    refresh().catch(error => { show(message, error.message, "danger"); });
})();
