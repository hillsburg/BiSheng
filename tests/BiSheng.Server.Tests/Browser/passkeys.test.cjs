const { test } = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const vm = require("node:vm");
const path = require("node:path");

const scripts = path.resolve(__dirname, "../../../src/BiSheng.Server/wwwroot/js");

// 最小浏览器适配层：按页面顺序执行生产脚本，验证用户可见结果与原生提交拦截。
function browser({ ids, credentials, fetch }) {
    const elements = new Map();
    function element(id) {
        const result = { id, value: "", textContent: "", disabled: false, hidden: false, dataset: {}, handlers: {}, children: [],
            addEventListener(type, handler) { this.handlers[type] = handler; },
            append(...children) { this.children.push(...children); },
            replaceChildren() { this.children = []; },
            reportValidity() { return true; },
            querySelectorAll() { return [...elements.values()].filter(e => e.id === "passkey-add" || e.id === "passkey-add-other" || e.id === "passkey-login"); }
        };
        elements.set(id, result);
        return result;
    }
    for (const id of ids) {
        element(id);
    }
    const calls = [];
    const navigations = [];
    const context = {
        document: {
            getElementById(id) { return elements.get(id) || null; },
            querySelector() { return { value: "antiforgery-token" }; },
            createElement() { return element("generated-" + elements.size); }
        },
        window: { isSecureContext: true, PublicKeyCredential: {} },
        navigator: { credentials },
        location: { assign(url) { navigations.push(url); } },
        async fetch(url, options) {
            calls.push([url, options]);
            return { ok: true, redirected: false, async json() { return fetch(url); } };
        },
        atob, btoa, Uint8Array, Date
    };
    return {
        elements, calls, navigations,
        run(...files) {
            for (const file of files) {
                vm.runInNewContext(fs.readFileSync(path.join(scripts, file), "utf8"), context);
            }
        }
    };
}

// 账号页：模拟一次添加流程，可选择在设备弹窗处取消。
function managePage(cancel = false) {
    let created = false;
    let prevented = false;
    const page = browser({
        ids: ["passkey-management", "passkey-form", "passkey-message", "passkey-add", "passkey-add-other", "passkey-list",
            "passkey-name", "passkey-password", "passkey-totp", "passkey-totp-group"],
        credentials: {
            async create() {
                if (cancel) {
                    throw Object.assign(new Error("cancel"), { name: "NotAllowedError" });
                }
                return { id: "AQ", rawId: new Uint8Array([1]), type: "public-key",
                    response: { clientDataJSON: new Uint8Array([2]), attestationObject: new Uint8Array([3]) },
                    getClientExtensionResults() { return {}; } };
            }
        },
        fetch(url) {
            if (url.endsWith("/register/options")) {
                return { challenge: "AQ", user: { id: "AQ" } };
            }
            if (url.endsWith("/register/complete")) {
                created = true;
                return { success: true };
            }
            return created ? [{ id: "key-1", name: "我的电脑", rpId: "localhost" }] : [];
        }
    });
    page.elements.get("passkey-name").value = "我的电脑";
    page.elements.get("passkey-password").value = "secret";
    page.elements.get("passkey-management").dataset = {
        development: "false", verifiedAt: String(Date.now() / 1000), verificationWindow: "300"
    };
    page.run("passkey-common.js", "passkey-manage.js");
    page.submit = (other = false) => page.elements.get("passkey-form").handlers.submit({
        submitter: page.elements.get(other ? "passkey-add-other" : "passkey-add"),
        preventDefault() { prevented = true; }
    });
    Object.defineProperty(page, "prevented", { get: () => prevented });
    return page;
}

async function settle() {
    for (let step = 0; step < 20; step++) {
        await new Promise(resolve => setImmediate(resolve));
    }
}

test("添加成功后保留页面、展示成功提示并更新列表", async () => {
    const page = managePage();
    await settle();
    page.submit();
    page.submit(); // 正在处理时再次点击不会重复发起仪式。
    await settle();
    assert.equal(page.prevented, true);
    assert.match(page.elements.get("passkey-message").textContent, /添加成功/);
    assert.equal(page.elements.get("passkey-password").value, "");
    assert.equal(page.elements.get("passkey-add").disabled, false);
    assert.equal(page.elements.get("passkey-totp-group").hidden, true);
    assert.equal(page.elements.get("passkey-list").children.length, 1);
    assert.equal(page.calls.filter(([url]) => url.endsWith("/register/complete")).length, 1);
});

test("取消设备验证时显示可重试提示且不保存凭据", async () => {
    const page = managePage(true);
    await settle();
    page.submit();
    await settle();
    assert.match(page.elements.get("passkey-message").textContent, /已取消或验证超时/);
    assert.equal(page.elements.get("passkey-password").value, "");
    assert.equal(page.elements.get("passkey-add").disabled, false);
    assert.equal(page.calls.some(([url]) => url.endsWith("/register/complete")), false);
});

test("登录时取消设备验证显示登录相关提示并可重试", async () => {
    const page = browser({
        ids: ["passkey-login", "passkey-message"],
        credentials: { async get() { throw Object.assign(new Error("cancel"), { name: "NotAllowedError" }); } },
        fetch() { return { challenge: "AQ" }; }
    });
    page.run("passkey-common.js", "passkey-login.js");
    page.elements.get("passkey-login").handlers.click();
    await settle();
    const text = page.elements.get("passkey-message").textContent;
    assert.match(text, /已取消或验证超时/);
    assert.doesNotMatch(text, /添加/);
    assert.equal(page.elements.get("passkey-login").disabled, false);
    assert.equal(page.calls.some(([url]) => url.endsWith("/login/complete")), false);
    assert.deepEqual(page.navigations, []);
});

// 验证两种按钮将明确的选择传给后端，而不是依赖浏览器自行选择认证器。
for (const other of [false, true]) {
    test(other ? "其他设备入口发送跨设备注册选项" : "本机入口发送内置认证器注册选项", async () => {
        const page = managePage();
        await settle();
        page.submit(other);
        await settle();
        const [, request] = page.calls.find(([url]) => url.endsWith("/register/options"));
        assert.equal(JSON.parse(request.body).useOtherDevice, other);
        assert.equal(page.elements.get("passkey-add-other").disabled, false);
    });
}
