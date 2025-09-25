// Program.cs
// Proyecto: ClinicaSimulada
// Framework: .NET 6+
// Autor: (Generado por ChatGPT)
// Nota: prototipo de consola que implementa la lógica del enunciado (roles, pacientes, ordenes, facturacion)

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

#region Modelos

enum Role { RecursosHumanos, Administrativo, Soporte, Enfermera, Medico }

class User
{
    public string Username { get; set; }
    public Role Role { get; set; }
    public string Password { get; set; } // En prototipo; en producción almacenar hash.
}

class EmergencyContact
{
    public string Nombre { get; set; }
    public string Relacion { get; set; }
    public string Telefono { get; set; }
}

class Insurance
{
    public string Company { get; set; }
    public string PolicyNumber { get; set; }
    public bool Activa { get; set; }
    public DateTime VigenciaFin { get; set; }
}

class Patient
{
    public string Cedula { get; set; }                  // clave unica
    public string NombreCompleto { get; set; }
    public DateTime FechaNacimiento { get; set; }
    public string Genero { get; set; }
    public string Direccion { get; set; }
    public string Telefono { get; set; }
    public string Email { get; set; }
    public EmergencyContact ContactoEmergencia { get; set; } // minimo y maximo 1
    public Insurance Poliza { get; set; } // puede ser null
    // Para facturación: llevamos seguimiento del copago pagado por año
    public Dictionary<int, decimal> CopagoPorAno { get; set; } = new Dictionary<int, decimal>();
}

abstract class OrderItem
{
    public int NumeroItem { get; set; } // dentro de la orden
    public string Nombre { get; set; }
    public decimal Costo { get; set; }
}

class MedicationItem : OrderItem
{
    public string Dosis { get; set; }
    public int DuracionDias { get; set; }
}

class ProcedureItem : OrderItem
{
    public int Veces { get; set; }
    public string Frecuencia { get; set; }
    public bool RequiereEspecialista { get; set; }
    public string IdTipoEspecialidad { get; set; } // opcional
}

class DiagnosticItem : OrderItem
{
    public int Cantidad { get; set; }
    public bool RequiereEspecialista { get; set; }
    public string IdTipoEspecialidad { get; set; }
}

class Order
{
    public int NumeroOrden { get; set; } // unico, max 6 digitos
    public string CedulaPaciente { get; set; }
    public string CedulaMedico { get; set; }
    public DateTime FechaCreacion { get; set; }
    public List<OrderItem> Items { get; set; } = new List<OrderItem>();
    // tipo de orden: medicamento/procedimiento/ayuda diagnostica (pueden mezclarse salvo regla de diagnostico)
}

class MedicalRecordEntry
{
    public DateTime Fecha { get; set; } // subclave de historia clinica
    public string CedulaMedico { get; set; }
    public string MotivoConsulta { get; set; }
    public string Sintomatologia { get; set; }
    public string Diagnostico { get; set; }
    public List<Order> OrdenesAsociadas { get; set; } = new List<Order>();
    public Dictionary<string, string> Observaciones { get; set; } = new Dictionary<string, string>();
}

#endregion

#region RepositoriosSimples (InMemory)

static class Repo
{
    // Usuarios
    public static List<User> Users = new List<User>();

    // Pacientes por cedula
    public static Dictionary<string, Patient> Patients = new Dictionary<string, Patient>();

    // Ordenes globales por numero de orden
    public static Dictionary<int, Order> Orders = new Dictionary<int, Order>();

    // Historia clinica: diccionario NoSQL estilo: cedula -> lista de entradas (subclave: fecha)
    public static Dictionary<string, List<MedicalRecordEntry>> MedicalHistory = new Dictionary<string, List<MedicalRecordEntry>>();

    // Generador simple de numero de orden (evitar colisiones)
    private static Random _rnd = new Random();
    public static int GenerateUniqueOrderNumber()
    {
        int num;
        int attempts = 0;
        do
        {
            num = _rnd.Next(1, 999999); // max 6 digitos
            attempts++;
            if (attempts > 10000) throw new Exception("No se pudo generar numero de orden unico.");
        } while (Orders.ContainsKey(num));
        return num;
    }
}

#endregion

#region UtilidadesValidacion

static class Validaciones
{
    public static bool ValidarCedula(string cedula) => !string.IsNullOrWhiteSpace(cedula) && cedula.Length <= 10 && cedula.All(char.IsDigit);
    public static bool ValidarTelefono(string tel) => !string.IsNullOrWhiteSpace(tel) && tel.Length == 10 && tel.All(char.IsDigit);
    public static bool ValidarEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email)) return true; // email opcional para paciente
        try
        {
            return Regex.IsMatch(email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$");
        }
        catch { return false; }
    }
    public static bool ValidarFechaNacimiento(DateTime fecha)
    {
        var age = DateTime.Today.Year - fecha.Year;
        if (fecha > DateTime.Today) return false;
        if (fecha.AddYears(age) > DateTime.Today) age--;
        return age <= 150 && age >= 0;
    }
    public static bool ValidarNombreUsuario(string u) => !string.IsNullOrWhiteSpace(u) && u.Length <= 15 && Regex.IsMatch(u, @"^[a-zA-Z0-9]+$");
    public static bool ValidarPassword(string p) => !string.IsNullOrEmpty(p) && p.Length >= 8 && p.Any(char.IsUpper) && p.Any(char.IsDigit) && p.Any(ch => !char.IsLetterOrDigit(ch));
}

#endregion

#region LogicaNegocio

static class ClinicaLogic
{
    // Recursos Humanos: crear usuario
    public static bool CrearUsuario(string username, string password, Role role, out string mensaje)
    {
        if (!Validaciones.ValidarNombreUsuario(username)) { mensaje = "Usuario inválido (max 15, solo letras y números)."; return false; }
        if (!Validaciones.ValidarPassword(password)) { mensaje = "Contraseña inválida (mínimo 8, 1 mayúscula, 1 número, 1 caracter especial)."; return false; }
        if (Repo.Users.Any(u => u.Username.Equals(username, StringComparison.OrdinalIgnoreCase))) { mensaje = "Usuario ya existe."; return false; }
        Repo.Users.Add(new User { Username = username, Password = password, Role = role });
        mensaje = "Usuario creado con éxito.";
        return true;
    }

    // Administrativo: registrar paciente
    public static bool RegistrarPaciente(Patient p, out string mensaje)
    {
        if (!Validaciones.ValidarCedula(p.Cedula)) { mensaje = "Cédula inválida (hasta 10 dígitos)."; return false; }
        if (Repo.Patients.ContainsKey(p.Cedula)) { mensaje = "Paciente con esa cédula ya existe."; return false; }
        if (!Validaciones.ValidarTelefono(p.Telefono)) { mensaje = "Teléfono inválido (10 dígitos)."; return false; }
        if (!Validaciones.ValidarEmail(p.Email)) { mensaje = "Email inválido."; return false; }
        if (!Validaciones.ValidarFechaNacimiento(p.FechaNacimiento)) { mensaje = "Fecha de nacimiento inválida (máx 150 años)."; return false; }
        if (p.ContactoEmergencia == null) { mensaje = "Debe existir 1 contacto de emergencia."; return false; }
        if (!Validaciones.ValidarTelefono(p.ContactoEmergencia.Telefono)) { mensaje = "Teléfono contacto emergencia inválido (10 dígitos)."; return false; }
        // max direccion 30 caracteres
        if (!string.IsNullOrWhiteSpace(p.Direccion) && p.Direccion.Length > 30) { mensaje = "Dirección supera 30 caracteres."; return false; }

        Repo.Patients.Add(p.Cedula, p);
        mensaje = "Paciente registrado.";
        return true;
    }

    // Medico: crear orden - aplica validaciones de reglas
    public static bool CrearOrden(Order orden, out string mensaje)
    {
        // numero orden unico o generar
        if (orden.NumeroOrden <= 0)
        {
            orden.NumeroOrden = Repo.GenerateUniqueOrderNumber();
        }
        if (orden.NumeroOrden > 999999) { mensaje = "Número de orden supera 6 dígitos."; return false; }
        if (Repo.Orders.ContainsKey(orden.NumeroOrden)) { mensaje = "Número de orden ya existe."; return false; }
        if (!Repo.Patients.ContainsKey(orden.CedulaPaciente)) { mensaje = "Paciente no registrado."; return false; }

        // Regla: si orden contiene ayuda diagnostica no puede contener medic/proc (segun enunciado)
        bool hasDiag = orden.Items.Any(i => i is DiagnosticItem);
        bool hasMedOrProc = orden.Items.Any(i => i is MedicationItem || i is ProcedureItem);
        if (hasDiag && hasMedOrProc) { mensaje = "Una orden con ayuda diagnóstica no puede contener medicamentos ni procedimientos."; return false; }

        // verificar unicidad de item dentro de la orden
        var itemNums = orden.Items.Select(i => i.NumeroItem).ToList();
        if (itemNums.Count != itemNums.Distinct().Count()) { mensaje = "Numeración de ítems debe ser única dentro de la orden."; return false; }

        // Guardar orden
        Repo.Orders.Add(orden.NumeroOrden, orden);
        mensaje = $"Orden creada. Numero: {orden.NumeroOrden}";
        return true;
    }

    // Asociar orden a historia clinica (cuando se tenga diagnostico o resultado)
    public static void AsociarOrdenAHistoria(string cedulaPaciente, MedicalRecordEntry entry)
    {
        if (!Repo.MedicalHistory.ContainsKey(cedulaPaciente)) Repo.MedicalHistory[cedulaPaciente] = new List<MedicalRecordEntry>();
        Repo.MedicalHistory[cedulaPaciente].Add(entry);
    }

    // Facturación: calcular factura y aplicar reglas de copago/topping
    public static (decimal total, decimal copago, decimal aseguradora, string detalle) GenerarFactura(int numeroOrden)
    {
        if (!Repo.Orders.ContainsKey(numeroOrden)) throw new Exception("Orden no existe.");
        var orden = Repo.Orders[numeroOrden];
        var paciente = Repo.Patients[orden.CedulaPaciente];
        decimal total = orden.Items.Sum(i => i.Costo);
        decimal copago = 0m;
        decimal aseguradora = 0m;
        var anio = orden.FechaCreacion.Year;
        decimal copagoFijo = 50000m;

        if (paciente.Poliza != null && paciente.Poliza.Activa && paciente.Poliza.VigenciaFin >= orden.FechaCreacion)
        {
            // si ya supero tope anual de copagos ($1.000.000) -> no paga copago
            paciente.CopagoPorAno.TryGetValue(anio, out decimal copagoAcumulado);
            if (copagoAcumulado >= 1000000m)
            {
                copago = 0m;
                aseguradora = total;
            }
            else
            {
                // aplicar copago fijo: 50k
                copago = Math.Min(copagoFijo, total);
                aseguradora = total - copago;
                // actualizar acumulado
                paciente.CopagoPorAno[anio] = copagoAcumulado + copago;
                // si acumula > 1,000,000, futuras facturas ese año no pagaran copago
            }
        }
        else
        {
            // poliza inactiva o inexistente -> paciente paga todo
            copago = total;
            aseguradora = 0m;
        }

        string detalle = $"Total: {total:C2}, Copago paciente: {copago:C2}, Aseguradora: {aseguradora:C2}";
        return (total, copago, aseguradora, detalle);
    }
}

#endregion

#region InterfazConsola (Simulación)

class Program
{
    static void Main()
    {
        Console.WriteLine("=== Simulación Clinica - Protótipo ===");
        SeedData();

        bool salir = false;
        while (!salir)
        {
            Console.WriteLine("\nMenú principal:");
            Console.WriteLine("1) Recursos Humanos - Crear Usuario");
            Console.WriteLine("2) Administrativo - Registrar Paciente");
            Console.WriteLine("3) Médico - Crear Orden");
            Console.WriteLine("4) Enfermera - Registrar signos vitales y asociar historia");
            Console.WriteLine("5) Generar Factura por orden");
            Console.WriteLine("6) Listar Pacientes");
            Console.WriteLine("7) Listar Ordenes");
            Console.WriteLine("0) Salir");
            Console.Write("Opción: ");
            var opt = Console.ReadLine();
            switch (opt)
            {
                case "1": RH_CreateUser(); break;
                case "2": Admin_RegisterPatient(); break;
                case "3": Doctor_CreateOrder(); break;
                case "4": Nurse_RecordVitals(); break;
                case "5": Billing_GenerateInvoice(); break;
                case "6": ListPatients(); break;
                case "7": ListOrders(); break;
                case "0": salir = true; break;
                default: Console.WriteLine("Opción no válida."); break;
            }
        }

        Console.WriteLine("Fin de simulación.");
    }

    static void SeedData()
    {
        // usuario demo RH
        ClinicaLogic.CrearUsuario("hr_admin", "Passw0rd!", Role.RecursosHumanos, out _);
        ClinicaLogic.CrearUsuario("admin01", "Adm1n$Pass", Role.Administrativo, out _);
        ClinicaLogic.CrearUsuario("medico1", "DocPass1!", Role.Medico, out _);

        // paciente demo
        var p = new Patient
        {
            Cedula = "1234567890",
            NombreCompleto = "Juan Perez",
            FechaNacimiento = new DateTime(1990, 5, 10),
            Genero = "Masculino",
            Direccion = "Calle 1 #2-3",
            Telefono = "3001234567",
            Email = "juan.perez@example.com",
            ContactoEmergencia = new EmergencyContact { Nombre = "Maria Perez", Relacion = "Esposa", Telefono = "3007654321" },
            Poliza = new Insurance { Company = "Seguros SA", PolicyNumber = "POL123", Activa = true, VigenciaFin = DateTime.Today.AddMonths(6) }
        };
        ClinicaLogic.RegistrarPaciente(p, out _);
    }

    static void RH_CreateUser()
    {
        Console.Write("Nombre de usuario: ");
        var u = Console.ReadLine();
        Console.Write("Contraseña: ");
        var p = Console.ReadLine();
        Console.WriteLine("Rol: 0-RH,1-Admin,2-Soporte,3-Enfermera,4-Medico");
        var r = Console.ReadLine();
        if (Enum.TryParse<Role>(r, out Role role))
        {
            if (ClinicaLogic.CrearUsuario(u, p, role, out string m)) Console.WriteLine(m); else Console.WriteLine($"Error: {m}");
        }
        else Console.WriteLine("Rol inválido.");
    }

    static void Admin_RegisterPatient()
    {
        var paciente = new Patient();
        Console.Write("Cédula (solo dígitos, max 10): ");
        paciente.Cedula = Console.ReadLine();
        Console.Write("Nombre completo: ");
        paciente.NombreCompleto = Console.ReadLine();
        Console.Write("Fecha de nacimiento (yyyy-mm-dd): ");
        if (!DateTime.TryParse(Console.ReadLine(), out DateTime fn)) { Console.WriteLine("Fecha inválida."); return; }
        paciente.FechaNacimiento = fn;
        Console.Write("Género: ");
        paciente.Genero = Console.ReadLine();
        Console.Write("Dirección (max 30 chars): ");
        paciente.Direccion = Console.ReadLine();
        Console.Write("Teléfono (10 dígitos): ");
        paciente.Telefono = Console.ReadLine();
        Console.Write("Email (opcional): ");
        paciente.Email = Console.ReadLine();

        Console.WriteLine("Contacto de emergencia (OBLIGATORIO):");
        var ce = new EmergencyContact();
        Console.Write("Nombre contacto: "); ce.Nombre = Console.ReadLine();
        Console.Write("Relación: "); ce.Relacion = Console.ReadLine();
        Console.Write("Teléfono (10 dígitos): "); ce.Telefono = Console.ReadLine();
        paciente.ContactoEmergencia = ce;

        Console.Write("¿Tiene seguro? (s/n): ");
        var withIns = Console.ReadLine();
        if (!string.IsNullOrEmpty(withIns) && withIns.ToLower().StartsWith("s"))
        {
            var ins = new Insurance();
            Console.Write("Compañía: "); ins.Company = Console.ReadLine();
            Console.Write("Número póliza: "); ins.PolicyNumber = Console.ReadLine();
            Console.Write("Activa? (s/n): "); ins.Activa = Console.ReadLine().ToLower().StartsWith("s");
            Console.Write("Vigencia fin (yyyy-mm-dd): ");
            if (!DateTime.TryParse(Console.ReadLine(), out DateTime vf)) { Console.WriteLine("Fecha inválida."); vf = DateTime.Today; }
            ins.VigenciaFin = vf;
            paciente.Poliza = ins;
        }

        if (ClinicaLogic.RegistrarPaciente(paciente, out string msg)) Console.WriteLine(msg); else Console.WriteLine($"Error: {msg}");
    }

    static void Doctor_CreateOrder()
    {
        Console.Write("Cédula paciente: ");
        var ced = Console.ReadLine();
        if (!Repo.Patients.ContainsKey(ced)) { Console.WriteLine("Paciente no registrado."); return; }
        Console.Write("Cédula medico: "); var cedMed = Console.ReadLine();
        var orden = new Order { CedulaPaciente = ced, CedulaMedico = cedMed, FechaCreacion = DateTime.Now };

        Console.WriteLine("Agregar ítems a la orden. Escribe 'fin' en nombre para terminar.");
        int idx = 1;
        while (true)
        {
            Console.Write("Tipo ítem (m-med, p-proc, d-diag, fin-terminar): ");
            var t = Console.ReadLine();
            if (t == "fin") break;
            if (t == "m")
            {
                var mi = new MedicationItem();
                mi.NumeroItem = idx++;
                Console.Write("Nombre medicamento: "); mi.Nombre = Console.ReadLine();
                Console.Write("Dosis: "); mi.Dosis = Console.ReadLine();
                Console.Write("Duración (días): "); int.TryParse(Console.ReadLine(), out int dias); mi.DuracionDias = dias;
                Console.Write("Costo: "); decimal.TryParse(Console.ReadLine(), out decimal c); mi.Costo = c;
                orden.Items.Add(mi);
            }
            else if (t == "p")
            {
                var pi = new ProcedureItem();
                pi.NumeroItem = idx++;
                Console.Write("Nombre procedimiento: "); pi.Nombre = Console.ReadLine();
                Console.Write("Veces: "); int.TryParse(Console.ReadLine(), out int v); pi.Veces = v;
                Console.Write("Frecuencia: "); pi.Frecuencia = Console.ReadLine();
                Console.Write("Requiere especialista? (s/n): "); pi.RequiereEspecialista = Console.ReadLine().ToLower().StartsWith("s");
                Console.Write("Costo: "); decimal.TryParse(Console.ReadLine(), out decimal c); pi.Costo = c;
                orden.Items.Add(pi);
            }
            else if (t == "d")
            {
                var di = new DiagnosticItem();
                di.NumeroItem = idx++;
                Console.Write("Nombre ayuda diagnóstica: "); di.Nombre = Console.ReadLine();
                Console.Write("Cantidad: "); int.TryParse(Console.ReadLine(), out int cant); di.Cantidad = cant;
                Console.Write("Requiere especialista? (s/n): "); di.RequiereEspecialista = Console.ReadLine().ToLower().StartsWith("s");
                Console.Write("Costo: "); decimal.TryParse(Console.ReadLine(), out decimal c); di.Costo = c;
                orden.Items.Add(di);
            }
            else { Console.WriteLine("Tipo inválido."); }
        }

        if (ClinicaLogic.CrearOrden(orden, out string m)) Console.WriteLine(m); else Console.WriteLine($"Error: {m}");
    }

    static void Nurse_RecordVitals()
    {
        Console.Write("Cédula paciente: ");
        var ced = Console.ReadLine();
        if (!Repo.Patients.ContainsKey(ced)) { Console.WriteLine("Paciente no registrado."); return; }
        Console.Write("Cedula medico que atendió (opcional): ");
        var cedMed = Console.ReadLine();
        var entry = new MedicalRecordEntry
        {
            Fecha = DateTime.Now,
            CedulaMedico = cedMed,
            MotivoConsulta = "Visita enfermeria - signos vitales",
            Sintomatologia = "Registro de signos",
            Diagnostico = ""
        };
        Console.Write("Temperatura: "); var temp = Console.ReadLine();
        Console.Write("Presión arterial: "); var pres = Console.ReadLine();
        Console.Write("Pulso: "); var pul = Console.ReadLine();
        Console.Write("Nivel oxígeno: "); var ox = Console.ReadLine();
        entry.Observaciones["Temperatura"] = temp;
        entry.Observaciones["Presion"] = pres;
        entry.Observaciones["Pulso"] = pul;
        entry.Observaciones["Oxigeno"] = ox;
        ClinicaLogic.AsociarOrdenAHistoria(ced, entry);
        Console.WriteLine("Signos vitales registrados y asociados a la historia clínica.");
    }

    static void Billing_GenerateInvoice()
    {
        Console.Write("Número de orden: ");
        if (!int.TryParse(Console.ReadLine(), out int n)) { Console.WriteLine("Número inválido."); return; }
        try
        {
            var result = ClinicaLogic.GenerarFactura(n);
            Console.WriteLine("Factura generada:");
            Console.WriteLine(result.detalle);
        }
        catch (Exception ex) { Console.WriteLine($"Error: {ex.Message}"); }
    }

    static void ListPatients()
    {
        Console.WriteLine("Pacientes registrados:");
        foreach (var p in Repo.Patients.Values)
        {
            Console.WriteLine($"- {p.Cedula} | {p.NombreCompleto} | Tel: {p.Telefono} | Póliza: {(p.Poliza != null ? p.Poliza.PolicyNumber : "N/A")}");
        }
    }

    static void ListOrders()
    {
        Console.WriteLine("Órdenes registradas:");
        foreach (var o in Repo.Orders.Values)
        {
            Console.WriteLine($"Orden {o.NumeroOrden} | Paciente: {o.CedulaPaciente} | Fecha: {o.FechaCreacion} | Items: {o.Items.Count} | Total: {o.Items.Sum(i => i.Costo):C2}");
        }
    }
}

#endregion
