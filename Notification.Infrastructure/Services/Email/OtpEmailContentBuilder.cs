namespace Notification.Infrastructure.Services.Email;

using System;
using System.Net;

internal static class OtpEmailContentBuilder
{
    public static (string Subject, string HtmlBody) Build(
        string fullName,
        string otp,
        int expiresInMinutes,
        string purpose)
    {
        var displayName = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(fullName) ? "Customer" : fullName.Trim());
        var encodedOtp = WebUtility.HtmlEncode(otp);
        var normalizedPurpose = string.IsNullOrWhiteSpace(purpose) ? "registration" : purpose.ToLowerInvariant();

        string subject;
        string actionMessage;

        switch (normalizedPurpose)
        {
            case "password_reset":
                subject = "Nexus - Password Reset Verification Code";
                actionMessage = "We received a request to reset your password. Use the OTP code below to proceed...";
                break;

            case "seller_registration":
                subject = "Nexus - Seller Account Verification Code";
                actionMessage = "Please use this verification code to complete your Seller onboarding...";
                break;

            case "registration":
            default:
                subject = "Nexus - Your Verification Code (OTP)";
                actionMessage = "Thank you for registering an account on the Nexus Platform. Please use the verification code below to activate your account:";
                break;
        }

        var htmlBody = $$"""
            <!DOCTYPE html>
            <html>
            <head>
                <meta charset="utf-8">
                <style>
                    body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; background-color: #f9fafb; margin: 0; padding: 20px; }
                    .container { max-width: 560px; margin: 0 auto; background-color: #ffffff; border-radius: 8px; border: 1px solid #e5e7eb; padding: 32px; }
                    .header { text-align: center; border-bottom: 1px solid #f3f4f6; padding-bottom: 20px; margin-bottom: 24px; }
                    .title { font-size: 22px; font-weight: 700; color: #111827; margin: 0; }
                    .content { color: #374151; font-size: 15px; line-height: 1.6; }
                    .otp-box { background-color: #f3f4f6; border-radius: 6px; padding: 18px; text-align: center; margin: 28px 0; font-size: 32px; font-weight: 800; letter-spacing: 6px; color: #1f2937; }
                    .footer { font-size: 13px; color: #9ca3af; text-align: center; margin-top: 32px; border-top: 1px solid #f3f4f6; padding-top: 20px; }
                </style>
            </head>
            <body>
                <div class="container">
                    <div class="header">
                        <h1 class="title">Nexus Ecosystem</h1>
                    </div>
                    <div class="content">
                        <p>Hello <strong>{{displayName}}</strong>,</p>
                        <p>{{actionMessage}}</p>
                        <div class="otp-box">{{encodedOtp}}</div>
                        <p>This code will expire in <strong>{{expiresInMinutes}} minutes</strong>. For security reasons, please do not share this code with anyone.</p>
                        <p>If you did not request this, please ignore this email.</p>
                    </div>
                    <div class="footer">
                        <p>&copy; {{DateTime.UtcNow.Year}} Nexus E-Commerce & Auction Ecosystem. All rights reserved.</p>
                    </div>
                </div>
            </body>
            </html>
            """;

        return (subject, htmlBody);
    }
}
