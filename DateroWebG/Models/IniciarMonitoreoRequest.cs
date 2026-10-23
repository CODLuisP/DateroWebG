using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DateroWeb.Models
{
    public class IniciarMonitoreoRequest
    {
        public string Placa { get; set; }
        public string Usuario { get; set; }
        public string FechaIni { get; set; }
        public string Codasig { get; set; }
        public string Codruta { get; set; }
    }
}
