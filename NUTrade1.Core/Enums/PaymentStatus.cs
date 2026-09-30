namespace NUTrade1.Core;

/// <summary>
/// Status of a listing-fee payment. Written only by Cloud Functions: the client
/// reads its own payment doc but never mutates it.
/// </summary>
public enum PaymentStatus
{
    AwaitingPayment = 0,
    Paid,
    Failed,
    Expired,
}
