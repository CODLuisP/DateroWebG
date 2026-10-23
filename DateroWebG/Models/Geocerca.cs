namespace DateroWeb.Models
{
    public class Geocerca
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public double CenterLat { get; set; }
        public double CenterLon { get; set; }
        public bool Active { get; set; }
        public TipoGeocerca Tipo { get; set; }
    }

    public enum TipoGeocerca
    {
        Ida,
        Regreso
    }
}