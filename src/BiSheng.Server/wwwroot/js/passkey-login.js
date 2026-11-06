/* 登录页：使用通行密钥免用户名、免密码登录。依赖 passkey-common.js。 */
(() => {
    "use strict";
    const { toRequestOptions, serialize, request, isSupported, show } = window.BiShengPasskey;
    const button = document.getElementById("passkey-login");
    const message = document.getElementById("passkey-message");

    if (!isSupported()) {
        show(message, "当前浏览器或连接不支持通行密钥，请使用 HTTPS 和支持的浏览器。", "danger");
        button.disabled = true;
        return;
    }

    button.addEventListener("click", async () => {
        button.disabled = true;
        show(message, "请在设备弹窗中完成指纹、面容或 PIN 验证。");
        try {
            const options = await request("/login/options", {});
            const credential = await navigator.credentials.get({ publicKey: toRequestOptions(options) });
            const result = await request("/login/complete", serialize(credential));
            // 跳转期间保持按钮禁用，避免重复提交已消费的挑战。
            location.assign(result.redirect);
        } catch (error) {
            show(message, error.name === "NotAllowedError" ? "已取消或验证超时，可以重新尝试。" : error.message, "danger");
            button.disabled = false;
        }
    });
})();
