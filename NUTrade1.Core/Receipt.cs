using System.Globalization;
using System.Text;

namespace NUTrade1.Core;

/// <summary>One labelled line on a receipt, such as "Package: Additional".</summary>
public sealed record ReceiptLine(string Label, string Value);

/// <summary>
/// A record of money that moved, written so the student can read it and keep a copy.
/// The text is the receipt. Save and share send this exact wording.
/// </summary>
public sealed class Receipt
{
    public string Title { get; set; } = "NUTrade receipt";
    public string Reference { get; set; } = string.Empty;
    public long AmountCentavos { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public List<ReceiptLine> Lines { get; } = new();
    public string Note { get; set; } = string.Empty;

    public string AmountDisplay => Money.ToDisplay(Math.Abs(AmountCentavos));

    public string WhenDisplay =>
        OccurredAt.ToUniversalTime().ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);

    public string FileName
    {
        get
        {
            var raw = string.IsNullOrWhiteSpace(Reference) ? "receipt" : Reference;
            var cleaned = new string(raw.Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
            if (cleaned.Length == 0) cleaned = "receipt";
            return $"nutrade-receipt-{cleaned}";
        }
    }

    public string ToText()
    {
        var text = new StringBuilder();
        text.AppendLine("NUTrade");
        text.AppendLine(Title);
        text.AppendLine($"Reference: {Reference}");
        text.AppendLine($"When: {WhenDisplay}");
        text.AppendLine($"Amount: {AmountDisplay}");
        foreach (var line in Lines)
            text.AppendLine($"{line.Label}: {line.Value}");
        if (!string.IsNullOrWhiteSpace(Note))
        {
            text.AppendLine();
            text.AppendLine(Note);
        }
        return text.ToString().TrimEnd() + Environment.NewLine;
    }
}

/// <summary>Builds a receipt for each kind of money movement in the app.</summary>
public static class Receipts
{
    public static Receipt ListingFee(string listingId, string packageName, long amountCentavos, DateTimeOffset when)
    {
        var receipt = new Receipt
        {
            Title = "Listing fee",
            Reference = listingId,
            AmountCentavos = amountCentavos,
            OccurredAt = when,
            Note = amountCentavos == 0
                ? "No charge. The first listing is free. NUTrade has no subscription."
                : "One-time posting fee. NUTrade has no subscription.",
        };
        receipt.Lines.Add(new ReceiptLine("Package", packageName));
        receipt.Lines.Add(new ReceiptLine("Status", amountCentavos == 0 ? "No charge" : "Paid"));
        return receipt;
    }

    public static Receipt BidDeposit(
        string intentId,
        string listingTitle,
        long depositCentavos,
        long bidCentavos,
        DateTimeOffset when,
        long creditAppliedCentavos = 0)
    {
        var receipt = new Receipt
        {
            Title = "Bid deposit",
            Reference = intentId,
            AmountCentavos = depositCentavos,
            OccurredAt = when,
            Note = "Commitment deposit, not the item price. You pay the full price in person if you win.",
        };
        receipt.Lines.Add(new ReceiptLine("Listing", listingTitle));
        receipt.Lines.Add(new ReceiptLine("Bid", Money.ToDisplay(bidCentavos)));
        if (creditAppliedCentavos > 0)
            receipt.Lines.Add(new ReceiptLine("Bid credit applied", Money.ToDisplay(creditAppliedCentavos)));
        var scanned = depositCentavos - Math.Max(0, creditAppliedCentavos);
        if (scanned > 0)
            receipt.Lines.Add(new ReceiptLine("Paid by QR", Money.ToDisplay(scanned)));
        receipt.Lines.Add(new ReceiptLine("Status", scanned > 0 ? "Paid" : "Covered by bid credit"));
        return receipt;
    }

    public static Receipt Ledger(LedgerEntry entry)
    {
        var receipt = new Receipt
        {
            Title = entry.KindDisplay,
            Reference = string.IsNullOrWhiteSpace(entry.Id) ? entry.Uid : entry.Id,
            AmountCentavos = entry.AmountCentavos,
            OccurredAt = entry.CreatedAt ?? DateTimeOffset.UnixEpoch,
            Note = "Bid credit stays in the app. It pays a later deposit and cannot be cashed out.",
        };
        if (!string.IsNullOrWhiteSpace(entry.Note))
            receipt.Lines.Add(new ReceiptLine("Detail", entry.Note));
        if (!string.IsNullOrWhiteSpace(entry.ListingId))
            receipt.Lines.Add(new ReceiptLine("Listing", entry.ListingId));
        return receipt;
    }

    public static Receipt Handover(string chatId, string listingTitle, long amountCentavos, DateTimeOffset when)
    {
        var receipt = new Receipt
        {
            Title = "Meetup handover",
            Reference = chatId,
            AmountCentavos = amountCentavos,
            OccurredAt = when,
            Note = "Both sides confirmed the meetup. The item price was paid in person.",
        };
        receipt.Lines.Add(new ReceiptLine("Listing", listingTitle));
        receipt.Lines.Add(new ReceiptLine("Status", "Completed"));
        return receipt;
    }

    public static Receipt OrderPayment(string reference, string listingTitle, long amountCentavos, DateTimeOffset when)
    {
        var receipt = new Receipt
        {
            Title = "Order payment",
            Reference = reference,
            AmountCentavos = amountCentavos,
            OccurredAt = when,
            Note = "Paid through QR Ph.",
        };
        receipt.Lines.Add(new ReceiptLine("Listing", listingTitle));
        receipt.Lines.Add(new ReceiptLine("Status", "Paid"));
        return receipt;
    }
}
