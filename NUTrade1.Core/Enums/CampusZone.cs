namespace NUTrade1.Core;

/// <summary>
/// A physical meetup point on campus where a trade is handed over.
///
/// <see cref="Other"/> is the escape hatch: the seller must then type the place
/// themselves, which lands in <c>CampusZoneOther</c> on the listing. Listings created
/// before this list was set (Main Library, Student Hub, Ver's Nest Booth, Main Lobby)
/// read back as <see cref="Unknown"/>, since enums travel as their member name.
/// </summary>
public enum CampusZone
{
    Unknown = 0,
    StudentLounge,
    Gymnasium,
    AccountingAndRegistrarOffice,
    Itso,
    Sdao,
    Avr,
    Other,
}
