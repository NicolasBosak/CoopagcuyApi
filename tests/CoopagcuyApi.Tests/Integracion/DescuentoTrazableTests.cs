using System.Net;
using System.Net.Http.Json;
using Azure.Storage.Blobs;
using CoopagcuyApi.Common;
using CoopagcuyApi.Features.Pagos.DTOs;
using CoopagcuyApi.Tests.Infra;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace CoopagcuyApi.Tests.Integracion;

/// <summary>
/// Un descuento solo puede apoyarse en una novedad que el CAT registró sobre
/// un cuy de ESA productora en ESE lote. Sin novedad de origen no hay
/// descuento: es la garantía entera de la feature.
/// </summary>
[Collection(ColeccionApi.Nombre)]
public class DescuentoTrazableTests(ApiFactory api) : IAsyncLifetime
{
    public Task InitializeAsync() => api.LimpiarAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private const string CedulaA = "0104576277";
    private const string CedulaB = "0102030405";

    [Fact]
    public async Task ElMontoPagadoSaleDeLaRestaDelServidor()
    {
        var (pagoId, novedadId) = await Sembrador.PagoConNovedadAsync(api, CedulaA, 120m);

        var respuesta = await api.ComoOperadorFaenamiento()
            .PostAsJsonAsync($"/api/pagos/{pagoId}/pagar", new
            {
                descuentos = new[] { new
                {
                    novedadCatId = novedadId,
                    descripcion = "llegó con la lesión abierta",
                    montoUsd = 17m
                }},
                comprobanteBase64 = Sembrador.ComprobanteBase64,
                pagadoPor = "Operador de planta"
            });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);

        await using var db = api.NuevoDbContext();
        var pago = await db.Pagos.AsNoTracking().FirstAsync(p => p.Id == pagoId);

        pago.MontoPagadoUsd.ShouldBe(103m);
        pago.Estado.ShouldBe(EstadoPago.Pagado);
        pago.ComprobanteUrl.ShouldNotBeNull();
        pago.PagadoPor.ShouldBe("Operador de planta");

        // La fila del descuento es EL artefacto de la feature: sin ella la
        // CAT recibe un pago rebajado y ningún registro de qué defecto lo
        // justifica. Que el ticket cuadre no prueba que se haya escrito;
        // borrar el Add dejaba el resto de la clase en verde.
        var descuentos = await db.Descuentos.AsNoTracking()
            .Where(d => d.PagoId == pagoId)
            .ToListAsync();

        descuentos.Count.ShouldBe(1);
        descuentos[0].NovedadCatId.ShouldBe(novedadId);
        descuentos[0].MontoUsd.ShouldBe(17m);
        descuentos[0].Descripcion.ShouldBe("llegó con la lesión abierta");
        descuentos[0].RegistradoPor.ShouldBe("Operador de planta");
    }

    [Fact]
    public async Task UnaNovedadDeOtraProductoraSeRechaza()
    {
        // El corazón de la trazabilidad: la planta no puede citar el defecto
        // de otra para descontarle a esta.
        var (pagoId, _) = await Sembrador.PagoConNovedadAsync(api, CedulaA, 120m);
        var (_, novedadAjena) = await Sembrador.PagoConNovedadAsync(api, CedulaB, 90m);

        var respuesta = await api.ComoOperadorFaenamiento()
            .PostAsJsonAsync($"/api/pagos/{pagoId}/pagar", new
            {
                descuentos = new[] { new
                {
                    novedadCatId = novedadAjena,
                    descripcion = "defecto que no es de esta productora",
                    montoUsd = 10m
                }},
                comprobanteBase64 = Sembrador.ComprobanteBase64,
                pagadoPor = "Operador de planta"
            });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        await using var db = api.NuevoDbContext();
        var pago = await db.Pagos.AsNoTracking().FirstAsync(p => p.Id == pagoId);
        pago.Estado.ShouldBe(EstadoPago.Pendiente);
        pago.ComprobanteUrl.ShouldBeNull();
    }

    [Fact]
    public async Task UnaNovedadSinCuyAsociadoSeRechaza()
    {
        // Las novedades de entrega (SinAyuno) no pertenecen a ningún animal:
        // no se puede descontar un cuy que no existe.
        var (pagoId, _) = await Sembrador.PagoConNovedadAsync(api, CedulaA, 120m);

        int novedadDeEntrega;
        await using (var db = api.NuevoDbContext())
        {
            var pago = await db.Pagos.AsNoTracking().FirstAsync(p => p.Id == pagoId);
            var novedad = new CoopagcuyApi.Features.Recepcion.Models.Novedad
            {
                LoteId = pago.LoteId!.Value,
                Tipo = TipoNovedad.SinAyuno,
                Descripcion = "la entrega no venía en ayunas",
                RegistradoPor = "Operadora de prueba"
            };
            db.Novedades.Add(novedad);
            await db.SaveChangesAsync();
            novedadDeEntrega = novedad.Id;
        }

        var respuesta = await api.ComoOperadorFaenamiento()
            .PostAsJsonAsync($"/api/pagos/{pagoId}/pagar", new
            {
                descuentos = new[] { new
                {
                    novedadCatId = novedadDeEntrega,
                    descripcion = "descuento sin animal",
                    montoUsd = 10m
                }},
                comprobanteBase64 = Sembrador.ComprobanteBase64,
                pagadoPor = "Operador de planta"
            });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task LaSumaDeDescuentosNoPuedeSuperarElTicket()
    {
        var (pagoId, novedadId) = await Sembrador.PagoConNovedadAsync(api, CedulaA, 120m);

        var respuesta = await api.ComoOperadorFaenamiento()
            .PostAsJsonAsync($"/api/pagos/{pagoId}/pagar", new
            {
                descuentos = new[] { new
                {
                    novedadCatId = novedadId,
                    descripcion = "descuento mayor que el ticket",
                    montoUsd = 500m
                }},
                comprobanteBase64 = Sembrador.ComprobanteBase64,
                pagadoPor = "Operador de planta"
            });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task UnDescuentoQueIgualaElTicketSeRechaza()
    {
        // FormPagoProductora.tsx ya rechaza `aPagar <= 0` en el cliente, pero
        // el servicio solo comparaba `total > pago.MontoUsd`: un descuento
        // que iguala el ticket (100% de rebaja) pasaba limpio y dejaba un
        // pago "Pagado" de $0.00 con una captura de una transferencia de $0
        // — algo que ya no es un pago. Este es el espejo en el servidor de
        // la misma regla, para que un cliente que se salte la pantalla no
        // pueda colarlo.
        var (pagoId, novedadId) = await Sembrador.PagoConNovedadAsync(api, CedulaA, 120m);

        var respuesta = await api.ComoOperadorFaenamiento()
            .PostAsJsonAsync($"/api/pagos/{pagoId}/pagar", new
            {
                descuentos = new[] { new
                {
                    novedadCatId = novedadId,
                    descripcion = "descuento igual al ticket entero",
                    montoUsd = 120m
                }},
                comprobanteBase64 = Sembrador.ComprobanteBase64,
                pagadoPor = "Operador de planta"
            });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        await using var db = api.NuevoDbContext();
        var pago = await db.Pagos.AsNoTracking().FirstAsync(p => p.Id == pagoId);
        pago.Estado.ShouldBe(EstadoPago.Pendiente);
        pago.MontoPagadoUsd.ShouldBeNull();
        pago.ComprobanteUrl.ShouldBeNull();
    }

    [Fact]
    public async Task NoSePuedePagarDosVecesElMismoTicket()
    {
        var (pagoId, novedadId) = await Sembrador.PagoConNovedadAsync(api, CedulaA, 120m);

        object Cuerpo() => new
        {
            descuentos = new[] { new
            {
                novedadCatId = novedadId,
                descripcion = "lesión",
                montoUsd = 10m
            }},
            comprobanteBase64 = Sembrador.ComprobanteBase64,
            pagadoPor = "Operador de planta"
        };

        var primera = await api.ComoOperadorFaenamiento()
            .PostAsJsonAsync($"/api/pagos/{pagoId}/pagar", Cuerpo());
        primera.StatusCode.ShouldBe(HttpStatusCode.OK);

        var segunda = await api.ComoOperadorFaenamiento()
            .PostAsJsonAsync($"/api/pagos/{pagoId}/pagar", Cuerpo());
        segunda.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task UnComprobanteInvalidoNoDejaBlobsHuerfanos()
    {
        // Se cuenta por DIFERENCIA DE BLOBS y no por filas: una prueba que
        // solo mira filas pasa con el fallo presente. Ya ocurrió una vez.
        var (pagoId, novedadId) = await Sembrador.PagoConNovedadAsync(api, CedulaA, 120m);
        var (_, novedadAjena) = await Sembrador.PagoConNovedadAsync(api, CedulaB, 90m);

        var antes = await ContarBlobsAsync();

        var respuesta = await api.ComoOperadorFaenamiento()
            .PostAsJsonAsync($"/api/pagos/{pagoId}/pagar", new
            {
                descuentos = new[] { new
                {
                    novedadCatId = novedadId,
                    descripcion = "lesión",
                    montoUsd = 10m
                }},
                comprobanteBase64 = "esto-no-es-base64-valido!!!",
                pagadoPor = "Operador de planta"
            });

        // El conteo va ANTES del status a propósito: Shouldly corta en la
        // primera aserción que falla, así que con el status delante una
        // mutación que devuelva el código correcto y filtre el blob pasaría
        // inadvertida. Esta mitad es la que fija el orden de las operaciones.
        (await ContarBlobsAsync()).ShouldBe(antes);
        respuesta.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // Segundo rechazo, con la captura BIEN formada y el descuento mal.
        // Sin este bloque la prueba solo fijaría que la captura se valida
        // antes de subir, y pasaría igual con la subida adelantada por
        // encima de la validación de descuentos —que es justo el fallo que
        // dejó blobs huérfanos—: el 400 del base64 salta antes y la subida
        // nunca llega a ejecutarse. Esta mitad es la que recorre ese tramo.
        var conDescuentoAjeno = await api.ComoOperadorFaenamiento()
            .PostAsJsonAsync($"/api/pagos/{pagoId}/pagar", new
            {
                descuentos = new[] { new
                {
                    novedadCatId = novedadAjena,
                    descripcion = "defecto que no es de esta productora",
                    montoUsd = 10m
                }},
                comprobanteBase64 = Sembrador.ComprobanteBase64,
                pagadoPor = "Operador de planta"
            });

        (await ContarBlobsAsync()).ShouldBe(antes);
        conDescuentoAjeno.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task UnaNovedadConIdCeroSeRechazaSinDejarBlob()
    {
        // El id 0 no es un caso de laboratorio: es el valor por defecto de un
        // int, así que un cliente que omita el campo lo manda sin querer. Con
        // el centinela FirstOrDefault/!= 0 esta cita se colaba —"no encontré
        // ninguna inválida" y "la inválida es el 0" eran el MISMO valor—, se
        // subía el blob y el INSERT reventaba contra la FK: 500 y captura
        // huérfana. Se afirman las DOS mitades porque solo la del conteo
        // distingue el arreglo del fallo: un 500 y un 409 dejan la misma fila
        // (ninguna), pero no el mismo blob.
        var (pagoId, _) = await Sembrador.PagoConNovedadAsync(api, CedulaA, 120m);

        var antes = await ContarBlobsAsync();

        var respuesta = await api.ComoOperadorFaenamiento()
            .PostAsJsonAsync($"/api/pagos/{pagoId}/pagar", new
            {
                descuentos = new[] { new
                {
                    novedadCatId = 0,
                    descripcion = "cita una novedad que no existe",
                    montoUsd = 10m
                }},
                comprobanteBase64 = Sembrador.ComprobanteBase64,
                pagadoPor = "Operador de planta"
            });

        (await ContarBlobsAsync()).ShouldBe(antes);
        respuesta.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        await using var db = api.NuevoDbContext();
        var pago = await db.Pagos.AsNoTracking().FirstAsync(p => p.Id == pagoId);
        pago.Estado.ShouldBe(EstadoPago.Pendiente);
        pago.ComprobanteUrl.ShouldBeNull();
    }

    [Fact]
    public async Task UnMismoDefectoNoSeDescuentaDosVeces()
    {
        // Sin la guardia de duplicados, las dos filas llegan juntas al mismo
        // SaveChangesAsync, el índice único (PagoId, NovedadCatId) las
        // rechaza y la DbUpdateException salta DESPUÉS de subir la captura.
        //
        // Se afirma el MENSAJE y no solo el 409 porque el rescate de
        // DbUpdateException responde 409 también: mirando solo el código,
        // quitar la guardia previa no se notaría.
        var (pagoId, novedadId) = await Sembrador.PagoConNovedadAsync(api, CedulaA, 120m);

        var antes = await ContarBlobsAsync();

        var respuesta = await api.ComoOperadorFaenamiento()
            .PostAsJsonAsync($"/api/pagos/{pagoId}/pagar", new
            {
                descuentos = new[]
                {
                    new { novedadCatId = novedadId,
                          descripcion = "lesión abierta", montoUsd = 10m },
                    new { novedadCatId = novedadId,
                          descripcion = "la misma, otra vez", montoUsd = 5m }
                },
                comprobanteBase64 = Sembrador.ComprobanteBase64,
                pagadoPor = "Operador de planta"
            });

        (await ContarBlobsAsync()).ShouldBe(antes);
        respuesta.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await MensajeAsync(respuesta))
            .ShouldBe("Un mismo defecto no puede descontarse dos veces.");
    }

    [Fact]
    public async Task UnDescuentoNegativoSeRechaza()
    {
        // Sin el "> 0" un monto negativo SUMA: -50 sobre un ticket de 120
        // daría un MontoPagadoUsd de 170 y pasaría limpio por el tope, que
        // solo compara la suma CONTRA el monto y nunca por debajo de cero.
        var (pagoId, novedadId) = await Sembrador.PagoConNovedadAsync(api, CedulaA, 120m);

        var respuesta = await api.ComoOperadorFaenamiento()
            .PostAsJsonAsync($"/api/pagos/{pagoId}/pagar", new
            {
                descuentos = new[] { new
                {
                    novedadCatId = novedadId,
                    descripcion = "descuento negativo",
                    montoUsd = -50m
                }},
                comprobanteBase64 = Sembrador.ComprobanteBase64,
                pagadoPor = "Operador de planta"
            });

        // 400 y no 409: que -50 no es un descuento se sabe leyendo el cuerpo,
        // sin preguntarle nada al servidor.
        respuesta.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await MensajeAsync(respuesta))
            .ShouldBe("Un descuento debe ser mayor a cero.");

        await using var db = api.NuevoDbContext();
        var pago = await db.Pagos.AsNoTracking().FirstAsync(p => p.Id == pagoId);
        pago.Estado.ShouldBe(EstadoPago.Pendiente);
        pago.MontoPagadoUsd.ShouldBeNull();
    }

    [Fact]
    public async Task UnDescuentoSinDescripcionSeRechaza()
    {
        // La novedad del CAT dice lo que se vio al recibir; el descuento
        // tiene que decir lo que se vio al faenar. En blanco, la productora
        // recibe una rebaja que nadie explica.
        var (pagoId, novedadId) = await Sembrador.PagoConNovedadAsync(api, CedulaA, 120m);

        var respuesta = await api.ComoOperadorFaenamiento()
            .PostAsJsonAsync($"/api/pagos/{pagoId}/pagar", new
            {
                descuentos = new[] { new
                {
                    novedadCatId = novedadId,
                    descripcion = "   ",
                    montoUsd = 10m
                }},
                comprobanteBase64 = Sembrador.ComprobanteBase64,
                pagadoPor = "Operador de planta"
            });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await MensajeAsync(respuesta))
            .ShouldBe("Cada descuento debe decir qué se observó en la planta.");

        // Segunda palanca: el status por sí solo dice poco desde que el
        // rescate de DbUpdateException responde también por su cuenta. Que el
        // ticket siga pendiente y sin filas de descuento es lo que prueba que
        // la petición murió antes de escribir nada.
        await using var db = api.NuevoDbContext();
        var pago = await db.Pagos.AsNoTracking().FirstAsync(p => p.Id == pagoId);
        pago.Estado.ShouldBe(EstadoPago.Pendiente);
        (await db.Descuentos.AsNoTracking().CountAsync(d => d.PagoId == pagoId))
            .ShouldBe(0);
    }

    [Fact]
    public async Task UnDescuentoConMasDeDosDecimalesSeRechaza()
    {
        // MontoUsd y MontoPagadoUsd son HasPrecision(10,2): 10.005 se
        // guardaría redondeado a 10.01 mientras la resta se calcula con
        // 10.005, y la fila dejaría de justificar el monto que acompaña.
        var (pagoId, novedadId) = await Sembrador.PagoConNovedadAsync(api, CedulaA, 120m);

        var respuesta = await api.ComoOperadorFaenamiento()
            .PostAsJsonAsync($"/api/pagos/{pagoId}/pagar", new
            {
                descuentos = new[] { new
                {
                    novedadCatId = novedadId,
                    descripcion = "medio centavo",
                    montoUsd = 10.005m
                }},
                comprobanteBase64 = Sembrador.ComprobanteBase64,
                pagadoPor = "Operador de planta"
            });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await MensajeAsync(respuesta)).ShouldContain("más de dos");

        await using var db = api.NuevoDbContext();
        (await db.Descuentos.AsNoTracking().CountAsync(d => d.PagoId == pagoId))
            .ShouldBe(0);
    }

    [Fact]
    public async Task UnPagoSinResponsableSeRechaza()
    {
        // 400 y no 409: el cuerpo viene mal, no llega a destiempo. El
        // conteo de blobs comprueba que la guardia vive del lado correcto
        // de la subida: en blanco se detectaba al armar la fila, ya con la
        // captura arriba. Antes de esta guardia, "   " respondía 200 y
        // dejaba el ticket pagado sin decir por quién.
        var (pagoId, _) = await Sembrador.PagoConNovedadAsync(api, CedulaA, 120m);

        var antes = await ContarBlobsAsync();

        var respuesta = await api.ComoOperadorFaenamiento()
            .PostAsJsonAsync($"/api/pagos/{pagoId}/pagar", new
            {
                descuentos = Array.Empty<object>(),
                comprobanteBase64 = Sembrador.ComprobanteBase64,
                pagadoPor = "   "
            });

        (await ContarBlobsAsync()).ShouldBe(antes);
        respuesta.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // El texto separa esta guardia del 400 de ModelState, que respondería
        // un ProblemDetails con "errors" y dejaría la guardia sin fijar.
        (await MensajeAsync(respuesta))
            .ShouldBe("El pago debe decir quién lo registró en la planta.");
    }

    [Fact]
    public async Task UnDescuentoYaRegistradoNoDejaLaCapturaHuerfana()
    {
        // Única vía por HTTP para que reviente el SaveChangesAsync con todas
        // las guardias puestas: sembrar a mano la fila (PagoId, NovedadCatId)
        // que protege el índice único, dejando el ticket PENDIENTE. La
        // petición pasa las cinco reglas —la de duplicados solo mira dentro
        // de la propia petición— y muere al guardar, cuando la captura ya
        // está subida. Es el único tramo que la validación previa no puede
        // cubrir, y el motivo de que el rescate borre el blob.
        var (pagoId, novedadId) = await Sembrador.PagoConNovedadAsync(api, CedulaA, 120m);

        await using (var siembra = api.NuevoDbContext())
        {
            siembra.Descuentos.Add(
                new CoopagcuyApi.Features.Pagos.Models.DescuentoPago
                {
                    PagoId = pagoId,
                    NovedadCatId = novedadId,
                    Descripcion = "descuento ya registrado antes",
                    MontoUsd = 3m,
                    RegistradoPor = "Operadora de prueba"
                });
            await siembra.SaveChangesAsync();
        }

        var antes = await ContarBlobsAsync();

        var respuesta = await api.ComoOperadorFaenamiento()
            .PostAsJsonAsync($"/api/pagos/{pagoId}/pagar", new
            {
                descuentos = new[] { new
                {
                    novedadCatId = novedadId,
                    descripcion = "la misma novedad, otra vez",
                    montoUsd = 10m
                }},
                comprobanteBase64 = Sembrador.ComprobanteBase64,
                pagadoPor = "Operador de planta"
            });

        // El conteo va primero: es la mitad que fija que el rescate retire la
        // captura que ya había subido.
        (await ContarBlobsAsync()).ShouldBe(antes);
        respuesta.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        // El mensaje no adivina la causa y dice que se puede reintentar. El
        // anterior afirmaba que quizá ya estaba registrado, que para media
        // docena de fallos distintos era falso y desaconsejaba el reintento.
        (await MensajeAsync(respuesta)).ShouldContain("sigue pendiente");

        await using var db = api.NuevoDbContext();
        var pago = await db.Pagos.AsNoTracking().FirstAsync(p => p.Id == pagoId);
        pago.Estado.ShouldBe(EstadoPago.Pendiente);
        pago.ComprobanteUrl.ShouldBeNull();
    }

    [Fact]
    public async Task ElPagoExitosoSubeExactamenteUnBlob()
    {
        // Control POSITIVO del contador. ContarBlobsAsync fija el nombre
        // "comprobantes-pago" a mano, mientras el servicio lo resuelve desde
        // AzureBlob:ContainerComprobantes. Si la configuración de pruebas
        // llegara a definir esa clave, el ayudante crearía y contaría un
        // contenedor ajeno y siempre vacío: todos los ShouldBe(antes) de esta
        // clase pasarían valiendo 0 == 0, sin medir nada. Esta prueba se cae
        // primero y delata la desconexión.
        var (pagoId, novedadId) = await Sembrador.PagoConNovedadAsync(api, CedulaA, 120m);

        var antes = await ContarBlobsAsync();

        var respuesta = await api.ComoOperadorFaenamiento()
            .PostAsJsonAsync($"/api/pagos/{pagoId}/pagar", new
            {
                descuentos = new[] { new
                {
                    novedadCatId = novedadId,
                    descripcion = "lesión abierta",
                    montoUsd = 10m
                }},
                comprobanteBase64 = Sembrador.ComprobanteBase64,
                pagadoPor = "Operador de planta"
            });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ContarBlobsAsync()).ShouldBe(antes + 1);
    }

    [Fact]
    public async Task SinComprobanteNoSePuedeMarcarComoPagado()
    {
        // Un pago marcado sin su captura es peor que un error: la CAT no
        // tendría nada que verificar y el ticket quedaría bloqueado.
        var (pagoId, _) = await Sembrador.PagoConNovedadAsync(api, CedulaA, 120m);

        var respuesta = await api.ComoOperadorFaenamiento()
            .PostAsJsonAsync($"/api/pagos/{pagoId}/pagar", new
            {
                descuentos = Array.Empty<object>(),
                comprobanteBase64 = "",
                pagadoPor = "Operador de planta"
            });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // Se afirma el TEXTO del servicio, no solo el 400: es lo que separa
        // esta guardia del [Required] implícito de MVC, que respondería un
        // ProblemDetails con "errors" y dejaría la guardia sin fijar.
        (await MensajeAsync(respuesta))
            .ShouldBe("El pago debe adjuntar la captura de la transferencia.");
    }

    [Fact]
    public async Task SinDescuentosSePagaElTicketCompleto()
    {
        var (pagoId, _) = await Sembrador.PagoConNovedadAsync(api, CedulaA, 120m);

        var respuesta = await api.ComoOperadorFaenamiento()
            .PostAsJsonAsync($"/api/pagos/{pagoId}/pagar", new
            {
                descuentos = Array.Empty<object>(),
                comprobanteBase64 = Sembrador.ComprobanteBase64,
                pagadoPor = "Operador de planta"
            });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);

        await using var db = api.NuevoDbContext();
        var pago = await db.Pagos.AsNoTracking().FirstAsync(p => p.Id == pagoId);
        pago.MontoPagadoUsd.ShouldBe(120m);
    }

    /// Mensaje del cuerpo, ya decodificado. Se lee como JSON y no como texto
    /// crudo porque el serializador escapa los no-ASCII —"quién" viaja como
    /// "quién"—: comparar contra el cuerpo en bruto obligaría a escribir
    /// los acentos escapados dentro de la prueba.
    private static async Task<string> MensajeAsync(HttpResponseMessage respuesta)
    {
        var cuerpo = await respuesta.Content
            .ReadFromJsonAsync<Dictionary<string, string>>();
        return cuerpo is not null && cuerpo.TryGetValue("mensaje", out var m)
            ? m : string.Empty;
    }

    private static async Task<int> ContarBlobsAsync()
    {
        var cliente = new BlobServiceClient(ApiFactory.CadenaBlob);
        var contenedor = cliente.GetBlobContainerClient("comprobantes-pago");
        await contenedor.CreateIfNotExistsAsync();

        var total = 0;
        await foreach (var _ in contenedor.GetBlobsAsync()) total++;
        return total;
    }
}
