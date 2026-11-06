/* 通行密钥页面共用的编码与请求工具；只传递公钥协议数据。 */
(() => {
    "use strict";
    const root = "/api/admin/passkeys";

    // 显式转换二进制字段，兼容未实现 WebAuthn JSON 辅助 API 的浏览器。
    function decode(value) {
        return Uint8Array.from(atob(value.replace(/-/g, "+").replace(/_/g, "/")), c => c.charCodeAt(0));
    }
    function encode(value) {
        return btoa(String.fromCharCode(...new Uint8Array(value)))
            .replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
    }
    function decodeDescriptors(descriptors) {
        return (descriptors || []).map(descriptor => ({ ...descriptor, id: decode(descriptor.id) }));
    }

    /** 服务端登录挑战 → navigator.credentials.get 参数。 */
    function toRequestOptions(options) {
        return { ...options, challenge: decode(options.challenge), allowCredentials: decodeDescriptors(options.allowCredentials) };
    }

    /** 服务端绑定挑战 → navigator.credentials.create 参数。 */
    function toCreationOptions(options) {
        return {
            ...options,
            challenge: decode(options.challenge),
            user: { ...options.user, id: decode(options.user.id) },
            excludeCredentials: decodeDescriptors(options.excludeCredentials)
        };
    }

    /** 认证器结果 → 服务端请求体；字段名需与 Fido2 服务端模型一致。 */
    function serialize(credential) {
        const response = { clientDataJSON: encode(credential.response.clientDataJSON) };
        if (credential.response.attestationObject) {
            response.attestationObject = encode(credential.response.attestationObject);
            response.transports = credential.response.getTransports?.() || [];
        } else {
            response.authenticatorData = encode(credential.response.authenticatorData);
            response.signature = encode(credential.response.signature);
            response.userHandle = credential.response.userHandle ? encode(credential.response.userHandle) : null;
        }
        return {
            id: credential.id, rawId: encode(credential.rawId), type: credential.type,
            response, extensions: credential.getClientExtensionResults()
        };
    }

    /** 携带防伪令牌调用通行密钥接口；失败时抛出可直接展示的错误。 */
    async function request(path, body) {
        const token = document.querySelector('input[name="__RequestVerificationToken"]').value;
        const result = await fetch(root + path, {
            method: body === undefined ? "GET" : "POST", credentials: "same-origin",
            headers: { "Content-Type": "application/json", "RequestVerificationToken": token },
            body: body === undefined ? undefined : JSON.stringify(body)
        });
        if (!result.ok || result.redirected) {
            const data = await result.json().catch(() => ({}));
            throw new Error(result.status === 429 ? "操作过于频繁，请稍后重试。" :
                (data.message || "操作失败，请刷新页面后重试。"));
        }
        return result.json();
    }

    /** WebAuthn 只能在安全上下文中使用。 */
    function isSupported() {
        return Boolean(window.isSecureContext && window.PublicKeyCredential);
    }

    function show(element, text, kind = "info") {
        element.textContent = text;
        element.className = "alert alert-" + kind;
    }

    window.BiShengPasskey = Object.freeze({ toRequestOptions, toCreationOptions, serialize, request, isSupported, show });
})();
