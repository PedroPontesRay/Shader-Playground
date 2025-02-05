#if UNITY_PS4
using Sony.NP;
using System.Collections.Generic;

public static class PS4AgeRestrictionHelper
{
    //AgeRating
    const int USKAge = 6;
    const int ACBAge = 3;
    const int PEGIAge = 3;
    const int ClassindAge = 6;
    const int ESRBAge = 6;

    public const int defaultNA = 6;
    public const int defaultEU = 6;

    readonly static string[] PEGICountries = {
        "at", // Austria
        "be", // Belgium
        "bg", // Bulgaria
        "hr", // Croatia
        "cy", // Cyprus
        "cz", // Czech Republic
        "dk", // Denmark
        "fi", // Finland
        "fr", // France
        "gr", // Greece
        "hu", // Hungary
        "is", // Iceland
        "ie", // Ireland
        "il", // Israel
        "it", // Italy
        "lu", // Luxembourg
        "mt", // Malta
        "nl", // The Netherlands
        "no", // Norway
        "sk", // Slovakia
        "si", // Slovenia
        "pl", // Poland
        "pt", // Portugal
        "ro", // Romania
        "es", // Spain
        "se", // Sweden
        "ch", // Switzerland
        "gb", // United Kingdom
    };

    private static readonly string[] ESRBCountries = {
        "us", // United States
        "mx", // Mexico
        "ca", // Canada
    };

    public static AgeRestriction[] GetAgeRestrictionsEU()
    {
        List<AgeRestriction> ageRestrictions = new List<AgeRestriction>();

        // PEGI Countries
        for (int i = 0; i < PEGICountries.Length; i++)
        {
            ageRestrictions.Add(new AgeRestriction(PEGIAge, new Core.CountryCode(PEGICountries[i])));
        }

        // USK
        ageRestrictions.Add(new AgeRestriction(USKAge, new Core.CountryCode("de")));

        // ACB
        ageRestrictions.Add(new AgeRestriction(ACBAge, new Core.CountryCode("au")));
        return ageRestrictions.ToArray();
    }

    public static AgeRestriction[] GetAgeRestrictionsNA()
    {
        List<AgeRestriction> ageRestrictions = new List<AgeRestriction>();

        // Brazil
        ageRestrictions.Add(new AgeRestriction(ClassindAge, new Core.CountryCode("br")));

        // ESRB Countries
        for (int i = 0; i < ESRBCountries.Length; i++)
        {
            ageRestrictions.Add(new AgeRestriction(ESRBAge, new Core.CountryCode(ESRBCountries[i])));
        }

        return ageRestrictions.ToArray();
    }
}
#endif