using System.Text.RegularExpressions;

namespace ImmunizationSystem.Api.Shared.Phone;

/// <summary>
/// Normalizes Nigerian phone numbers to E.164 (+234...) so SMS providers (Twilio/Termii) can deliver reminders.
/// </summary>
public static partial class PhoneNumberFormatter
{
    /// <summary>
    /// Attempts to normalize a phone number to +234 E.164 format.
    /// Accepts local (0801...), bare country code (234801...), or already-normalized (+234801...) input.
    /// </summary>
    public static bool TryNormalizeToNigerianE164(string? input, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var digitsOnly = NonDigitPattern().Replace(input.Trim(), string.Empty);

        // Local format: 0XXXXXXXXXX (11 digits, starts with 0)
        if (digitsOnly.Length == 11 && digitsOnly.StartsWith('0'))
        {
            normalized = "+234" + digitsOnly[1..];
            return true;
        }

        // Bare country code: 234XXXXXXXXXX (13 digits)
        if (digitsOnly.Length == 13 && digitsOnly.StartsWith("234"))
        {
            normalized = "+" + digitsOnly;
            return true;
        }

        // Already has country code but was stripped of '+': same as above, covered.
        // Local number without leading 0 (10 digits): assume Nigerian mobile.
        if (digitsOnly.Length == 10)
        {
            normalized = "+234" + digitsOnly;
            return true;
        }

        return false;
    }

    [GeneratedRegex(@"[^\d]")]
    private static partial Regex NonDigitPattern();
}
