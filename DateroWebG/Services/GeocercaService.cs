using DateroWeb.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DateroWeb.Services
{
    public class GeocercaService
    {
        private readonly List<Geocerca> _geocercas;
        private readonly Dictionary<string, HashSet<int>> _geocercasVisitadas;
        private readonly Dictionary<string, ModoRuta> _modoRutaPorPlaca;

        public event Func<string, Task> OnCambioModoRegreso;

        public GeocercaService()
        {
            _geocercas = InicializarGeocercas();
            _geocercasVisitadas = new Dictionary<string, HashSet<int>>();
            _modoRutaPorPlaca = new Dictionary<string, ModoRuta>();
        }

        private List<Geocerca> InicializarGeocercas()
        {
            return new List<Geocerca>
            {
                // GEOCERCAS DE IDA (1-8)
                new Geocerca { Id = 1, Name = "JOSE GALVEZ IDA", CenterLat = -12.229356, CenterLon = -76.909526, Active = true, Tipo = TipoGeocerca.Ida },
                new Geocerca { Id = 2, Name = "TOTTUS IDA", CenterLat = -12.204771, CenterLon = -76.928908, Active = true, Tipo = TipoGeocerca.Ida },
                new Geocerca { Id = 3, Name = "CVA. ESPERANZA IDA", CenterLat = -12.180567, CenterLon = -76.943554, Active = true, Tipo = TipoGeocerca.Ida },
                new Geocerca { Id = 4, Name = "SAN JUAN IDA", CenterLat = -12.156817, CenterLon = -76.965253, Active = true, Tipo = TipoGeocerca.Ida },
                new Geocerca { Id = 5, Name = "CIUDAD IDA", CenterLat = -12.154306, CenterLon = -76.9718, Active = true, Tipo = TipoGeocerca.Ida },
                new Geocerca { Id = 6, Name = "CT IDA", CenterLat = -12.165292, CenterLon = -76.97273, Active = true, Tipo = TipoGeocerca.Ida },
                new Geocerca { Id = 7, Name = "AMERICA IDA", CenterLat = -12.184261, CenterLon = -76.97521, Active = true, Tipo = TipoGeocerca.Ida },
                new Geocerca { Id = 8, Name = "TUPAC IDA", CenterLat = -12.194796, CenterLon = -76.983054, Active = true, Tipo = TipoGeocerca.Ida }, //CAMBIA A MODO REGRESO
                
                // GEOCERCAS DE REGRESO (9-16)
                new Geocerca { Id = 9, Name = "AMERICA VUELTA", CenterLat = -12.184261, CenterLon = -76.97521, Active = true, Tipo = TipoGeocerca.Regreso },
                new Geocerca { Id = 10, Name = "CT VUELTA", CenterLat = -12.165292, CenterLon = -76.97273, Active = true, Tipo = TipoGeocerca.Regreso },
                new Geocerca { Id = 11, Name = "CIUDAD VUELTA", CenterLat = -12.154306, CenterLon = -76.9718, Active = true, Tipo = TipoGeocerca.Regreso },
                new Geocerca { Id = 12, Name = "SAN JUAN VUELTA", CenterLat = -12.156817, CenterLon = -76.965253, Active = true, Tipo = TipoGeocerca.Regreso },
                new Geocerca { Id = 13, Name = "CVA. ESPERANZA VUELTA", CenterLat = -12.180567, CenterLon = -76.943554, Active = true, Tipo = TipoGeocerca.Regreso },
                new Geocerca { Id = 14, Name = "TOTTUS VUELTA", CenterLat = -12.204771, CenterLon = -76.928908, Active = true, Tipo = TipoGeocerca.Regreso },
                new Geocerca { Id = 15, Name = "JOSE GALVEZ VUELTA", CenterLat = -12.229356, CenterLon = -76.909526, Active = true, Tipo = TipoGeocerca.Regreso },
                new Geocerca { Id = 16, Name = "PARADERO 01 VUELTA", CenterLat = -12.20373, CenterLon = -76.906196, Active = true, Tipo = TipoGeocerca.Regreso }, //SE LIBERA Y FINALIZA                  
            };
        }

        public void InicializarMonitoreoPlaca(string placa, ModoRuta modoInicial = ModoRuta.Ida)
        {
            if (!_geocercasVisitadas.ContainsKey(placa))
            {
                _geocercasVisitadas[placa] = new HashSet<int>();
            }

            // Arranque directo en vuelta: no se dispara OnCambioModoRegreso porque el despacho
            // ya dejó rutaact y feciniruta correctos en BD.
            if (modoInicial == ModoRuta.Regreso)
            {
                foreach (var g in _geocercas.Where(g => g.Tipo == TipoGeocerca.Ida))
                    _geocercasVisitadas[placa].Add(g.Id);
            }

            _modoRutaPorPlaca[placa] = modoInicial;
            Console.WriteLine($"[GEOCERCA] 🔄 Modo inicial: {modoInicial}");
        }

        public void LimpiarMonitoreoPlaca(string placa)
        {
            _geocercasVisitadas.Remove(placa);
            _modoRutaPorPlaca.Remove(placa);
        }

        public Geocerca VerificarCruceGeocerca(
    string placa,
    double longitudActual,
    double latitudActual)
        {
            if (!_geocercasVisitadas.ContainsKey(placa))
            {
                InicializarMonitoreoPlaca(placa);
            }

            if (!_modoRutaPorPlaca.ContainsKey(placa))
            {
                _modoRutaPorPlaca[placa] = ModoRuta.Ida;
            }

            var modoActual = _modoRutaPorPlaca[placa];

            foreach (var geocerca in _geocercas.Where(g => g.Active))
            {
                // Si ya visitó esta geocerca, saltarla
                if (_geocercasVisitadas[placa].Contains(geocerca.Id))
                    continue;

                if (geocerca.Id == 16) continue;

                // Solo verificar geocercas del modo actual
                if (modoActual == ModoRuta.Ida && geocerca.Tipo != TipoGeocerca.Ida)
                    continue;

                if (modoActual == ModoRuta.Regreso && geocerca.Tipo != TipoGeocerca.Regreso)
                    continue;

                // ✅ NUEVA LÓGICA: Detectar cuando cruza
                bool cruzo = false;

                if (modoActual == ModoRuta.Ida)
                {
                    // IDA: Detectar cruce de DERECHA → IZQUIERDA
                    // Si la longitud actual <= geocerca (cruzó hacia la izquierda)
                    cruzo = (longitudActual <= geocerca.CenterLon);
                }
                else // Regreso
                {
                    // REGRESO: Detectar cruce de IZQUIERDA → DERECHA
                    // Si la longitud actual >= geocerca (cruzó hacia la derecha)
                    cruzo = (longitudActual >= geocerca.CenterLon);
                }

                if (cruzo)
                {
                    _geocercasVisitadas[placa].Add(geocerca.Id);

                    Console.WriteLine($"\n[GEOCERCA] 🎯 ════════════════════════════════════");
                    Console.WriteLine($"[GEOCERCA]    ¡GEOCERCA DETECTADA!");
                    Console.WriteLine($"[GEOCERCA]    Geocerca: {geocerca.Name}");
                    Console.WriteLine($"[GEOCERCA]    Latitud actual: {latitudActual:F6}");
                    Console.WriteLine($"[GEOCERCA]    Longitud actual: {longitudActual:F6}");
                    Console.WriteLine($"[GEOCERCA]    Longitud geocerca: {geocerca.CenterLon:F6}");
                    Console.WriteLine($"[GEOCERCA]    Modo: {modoActual}");
                    Console.WriteLine($"[GEOCERCA] ════════════════════════════════════\n");

                    return geocerca;
                }
            }

            return null;
        }

        public ModoRuta ObtenerModoActual(string placa)
        {
            if (_modoRutaPorPlaca.ContainsKey(placa))
            {
                return _modoRutaPorPlaca[placa];
            }
            return ModoRuta.Ida;
        }

        // ← AGREGAR ESTE MÉTODO QUE FALTABA
        public HashSet<int> ObtenerGeocercasVisitadas(string placa)
        {
            if (_geocercasVisitadas.ContainsKey(placa))
            {
                return _geocercasVisitadas[placa];
            }
            return new HashSet<int>();
        }

        public int[] ObtenerMinutosDinamicos(DateTime horaDespacho)
        {
            return new[]
            {15, 27, 37, 49, 55, 62, 72, 90, 99, 110, 115, 135, 142, 150, 159, 163};  // ← VERIFICAR: 16 elementos
        }

        public void MarcarGeocercaVisitada(string placa, int geocercaId)
        {
            if (!_geocercasVisitadas.ContainsKey(placa))
            {
                _geocercasVisitadas[placa] = new HashSet<int>();
            }

            _geocercasVisitadas[placa].Add(geocercaId);
        }

        public void CambiarModo(string placa, ModoRuta nuevoModo)
        {
            _modoRutaPorPlaca[placa] = nuevoModo;

            // Invocar evento si cambia a REGRESO
            if (nuevoModo == ModoRuta.Regreso)
            {
                _ = OnCambioModoRegreso?.Invoke(placa);
            }
        }
    }

    public enum ModoRuta
    {
        Ida,
        Regreso
    }
}