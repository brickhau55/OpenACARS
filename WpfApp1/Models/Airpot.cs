public class Airport
{
    public string ICAO { get; set; } = "";
    public string IATA { get; set; } = "";
    public string Name { get; set; } = "";

    public override string ToString()
    {
        return $"{ICAO} - {Name}";
    }
}