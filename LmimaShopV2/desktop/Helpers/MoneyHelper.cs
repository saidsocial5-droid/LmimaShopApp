using System.Globalization;

namespace LmimaShopV2.Desktop.Helpers;

public static class MoneyHelper
{
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

    public static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    public static string FormatMad(decimal value) => value.ToString("N2", French) + " DH";

    public static string FormatRial(decimal value) => (value * 20m).ToString("N2", French) + " Rial";

    public static string FormatPlain(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    public static decimal ToRial(decimal mad) => Round(mad * 20m);
}
