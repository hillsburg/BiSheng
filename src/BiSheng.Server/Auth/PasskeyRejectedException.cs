namespace BiSheng.Server.Auth;

/// <summary>通行密钥流程的预期拒绝；消息直接展示给用户，不得包含凭据或账号细节。</summary>
public sealed class PasskeyRejectedException(string message, Exception? inner = null) : Exception(message, inner);
