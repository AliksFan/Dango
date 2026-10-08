using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;

namespace Dango.Server.Hosting;

internal static class ConfirmationPage
{
    // Email scanners can GET this page; only a deliberate, CSRF-protected POST consumes proof.
    internal static IResult Render(HttpContext context, IAntiforgery protection, bool reset)
    {
        var proof = context.Request.Query["proof"];
        if (proof.Count != 1 || proof[0] is not { Length: > 0 and <= 4096 }) return Results.BadRequest(new { code = "email_proof_invalid" });
        var token = protection.GetAndStoreTokens(context).RequestToken;
        var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
        context.Response.Headers["Content-Security-Policy"] = $"default-src 'none'; script-src 'nonce-{nonce}'; connect-src 'self'; form-action 'self'; base-uri 'none'; frame-ancestors 'none'";
        var title = reset ? "重设密码" : "验证邮箱";
        var input = reset ? "<label>新密码 <input id=\"password\" type=\"password\" minlength=\"12\" maxlength=\"128\" autocomplete=\"new-password\" required></label>" : string.Empty;
        var endpoint = reset ? "/api/v1/auth/password-reset/complete" : "/api/v1/auth/email-verifications/confirm";
        var success = reset ? "密码已更新，请回客户端重新登录。" : "邮箱已验证，请回原客户端完成注册。";
        var passwordField = reset ? ",password:document.getElementById('password').value" : string.Empty;
        var html = $$"""
            <!doctype html><html lang="zh-CN"><meta charset="utf-8"><meta name="viewport" content="width=device-width"><title>Dango {{title}}</title>
            <main><h1>{{title}}</h1><p>点击后提交确认。</p><form id="confirm">{{input}}<button type="submit">{{title}}</button></form><p id="result" role="status"></p></main>
            <script nonce="{{nonce}}">
            document.getElementById('confirm').addEventListener('submit',async event=>{
              event.preventDefault();const button=event.target.querySelector('button');button.disabled=true;
              try{const response=await fetch('{{endpoint}}',{method:'POST',headers:{'Content-Type':'application/json','X-CSRF-TOKEN':{{JsonSerializer.Serialize(token)}}},body:JSON.stringify({proof:{{JsonSerializer.Serialize(proof[0])}}{{passwordField}}})});
              document.getElementById('result').textContent=response.ok?'{{success}}':'确认失败，证明可能已到期或已使用，请返回客户端。';}
              catch{document.getElementById('result').textContent='无法连接服务，请稍后重试。';}finally{button.disabled=false;}
            });
            </script></html>
            """;
        return Results.Content(html, "text/html; charset=utf-8");
    }
}
