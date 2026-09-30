import { MailMessage } from "./brevo";
import { OTP_TTL_MINUTES, PASSWORD_RESET_TTL_MINUTES } from "./constants";

/**
 * The one card layout both code emails share, so a change to the NUTrade look lands in
 * both at once. Kept as inline-styled tables on purpose: Gmail and Outlook strip
 * <style> blocks, and a flex layout collapses into a single unreadable column.
 */
function codeCard(options: {
  heading: string;
  blurb: string;
  code: string;
  footer: string;
}): string {
  return `<!doctype html>
<html>
  <body style="margin:0;padding:24px;background:#F4F6FB;font-family:-apple-system,Segoe UI,Roboto,Helvetica,Arial,sans-serif;">
    <table role="presentation" width="100%" cellpadding="0" cellspacing="0">
      <tr><td align="center">
        <table role="presentation" width="100%" style="max-width:440px;background:#FFFFFF;border-radius:16px;padding:32px;">
          <tr><td>
            <p style="margin:0 0 4px;font-size:13px;letter-spacing:.08em;text-transform:uppercase;color:#8A93AD;">NUTrade</p>
            <h1 style="margin:0 0 16px;font-size:21px;color:#1C2C58;">${options.heading}</h1>
            <p style="margin:0 0 24px;font-size:15px;line-height:1.5;color:#41496A;">
              ${options.blurb}
            </p>
            <p style="margin:0 0 24px;font-size:34px;font-weight:700;letter-spacing:.22em;color:#1C2C58;">${options.code}</p>
            <p style="margin:0;font-size:13px;line-height:1.5;color:#8A93AD;">
              ${options.footer}
            </p>
          </td></tr>
        </table>
      </td></tr>
    </table>
  </body>
</html>`;
}

/** The registration / email-verification code. */
export function otpEmail(code: string): MailMessage {
  return {
    subject: `${code} is your NUTrade code`,
    text:
      `Your NUTrade verification code is ${code}.\n\n` +
      `It expires in ${OTP_TTL_MINUTES} minutes. If you didn't try to sign in to NUTrade, ` +
      `you can ignore this email — nobody can use the code without your account.\n`,
    html: codeCard({
      heading: "Confirm your email",
      blurb: "Enter this code in the app to finish setting up your account.",
      code,
      footer:
        `The code expires in ${OTP_TTL_MINUTES} minutes. If you didn't try to sign in to ` +
        `NUTrade, you can ignore this email.`,
    }),
  };
}

/**
 * The password-reset code. Deliberately blunt about what to do if it was unexpected:
 * this is the one email that, acted on by the wrong person, hands over an account.
 */
export function passwordResetEmail(code: string): MailMessage {
  return {
    subject: `${code} is your NUTrade password reset code`,
    text:
      `Your NUTrade password reset code is ${code}.\n\n` +
      `It expires in ${PASSWORD_RESET_TTL_MINUTES} minutes. If you didn't ask to reset your ` +
      `NUTrade password, ignore this email — your password has not changed.\n`,
    html: codeCard({
      heading: "Reset your password",
      blurb: "Enter this code in the app, then choose a new password.",
      code,
      footer:
        `The code expires in ${PASSWORD_RESET_TTL_MINUTES} minutes. If you didn't ask to ` +
        `reset your NUTrade password, ignore this email — your password has not changed.`,
    }),
  };
}
