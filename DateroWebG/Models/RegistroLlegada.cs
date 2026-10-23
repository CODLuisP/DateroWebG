using System;

namespace DateroWeb.Models
{
    public class RegistroLlegada
    {
        public string Placa { get; set; }
        public string Codasig { get; set; }
        public int GeocercaId { get; set; }
        public string GeocercaNombre { get; set; }
        public DateTime HoraDespacho { get; set; }
        public DateTime HoraEsperada { get; set; }
        public DateTime HoraLlegada { get; set; }
        public int DiferenciaMinutos { get; set; }
        public double Latitud { get; set; }
        public double Longitud { get; set; }
        public bool Enviado { get; set; }
    }
}