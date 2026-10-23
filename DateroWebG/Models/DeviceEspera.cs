namespace DateroWeb.Models
{
    public class DeviceEspera
    {
        public string Placa { get; set; }
        public string RutaAct { get; set; }
        public string CodConductorAct { get; set; }

        public bool TieneRutaActiva => !string.IsNullOrEmpty(RutaAct) && RutaAct != "0";
    }
}
