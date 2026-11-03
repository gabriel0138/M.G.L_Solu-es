using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using MglConnect.Web.Services;

namespace MglConnect.Web.Controllers.Api
{
    [ApiController]
    [Route("api/rastreio")]
    public class RastreioApiController : ControllerBase
    {
        private readonly ITrackingService _trackingService;

        public RastreioApiController(ITrackingService trackingService)
        {
            _trackingService = trackingService;
        }

        // GET: /api/rastreio/{codigo}
        [HttpGet("{codigo}")]
        public async Task<IActionResult> Consultar(string codigo)
        {
            var entrega = await _trackingService.GetTrackingDetailsAsync(codigo);
            if (entrega == null)
            {
                return NotFound(new { erro = "Rastreamento não localizado." });
            }

            return Ok(new
            {
                codigoRastreio = entrega.CodigoRastreio,
                status = entrega.StatusEntrega,
                destinatario = entrega.DestinatarioNome,
                cidadeOrigem = entrega.CidadeOrigem,
                ufOrigem = entrega.EstadoOrigem,
                cidadeDestino = entrega.CidadeDestino,
                ufDestino = entrega.EstadoDestino,
                dataPostagem = entrega.DataCriacao,
                previsaoEntrega = entrega.DataPrevisaoEntrega,
                dataEntregaRealizada = entrega.DataEntregaRealizada,
                eventos = entrega.EventosRastreio.Select(e => new
                {
                    dataHora = e.DataHora,
                    titulo = e.Titulo,
                    descricao = e.Descricao,
                    localidade = e.Localidade,
                    status = e.StatusAssociado,
                    icone = e.Icone
                })
            });
        }
    }
}
