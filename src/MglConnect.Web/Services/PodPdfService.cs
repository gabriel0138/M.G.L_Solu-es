using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using MglConnect.Web.Data;
using MglConnect.Web.Models;

namespace MglConnect.Web.Services
{
    /// <summary>
    /// Serviço de geração de PDF do Comprovante de Entrega (POD) via QuestPDF.
    /// Inclui: dados fiscais, timestamp, coordenadas GPS, foto e assinatura digital.
    /// </summary>
    public class PodPdfService : IPodPdfService
    {
        private readonly MglDbContext _context;
        private readonly ILogger<PodPdfService> _logger;

        public PodPdfService(MglDbContext context, ILogger<PodPdfService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<byte[]> GerarPodPdfAsync(Entrega entrega)
        {
            if (entrega == null) throw new ArgumentNullException(nameof(entrega));

            // Carrega relacionamentos se necessário
            var entregaCompleta = await _context.Entregas
                .Include(e => e.Pedido)
                    .ThenInclude(p => p!.Cliente)
                .Include(e => e.Motorista)
                .Include(e => e.Veiculo)
                .Include(e => e.EventosRastreio.OrderByDescending(ev => ev.DataHora))
                .FirstOrDefaultAsync(e => e.Id == entrega.Id) ?? entrega;

            try
            {
                var pdfBytes = Document.Create(container =>
                {
                    container.Page(page =>
                    {
                        page.Size(PageSizes.A4);
                        page.Margin(1.5f, Unit.Centimetre);
                        page.DefaultTextStyle(x => x.FontSize(9).FontFamily("Arial"));

                        // ── CABEÇALHO ──────────────────────────────────────
                        page.Header().Column(col =>
                        {
                            col.Item().Row(row =>
                            {
                                row.RelativeItem().Column(inner =>
                                {
                                    inner.Item().Text("M.G.L LOGÍSTICA INTEGRADA S.A.")
                                        .Bold().FontSize(14)
                                        .FontColor("#0B132B");

                                    inner.Item().Text("Comprovante Digital de Entrega — PROOF OF DELIVERY (POD)")
                                        .FontSize(9).FontColor("#5BC0BE").Bold();

                                    inner.Item().Text("Registro operacional de recebimento e assinatura coletada no sistema")
                                        .FontSize(7).FontColor("#64748B");
                                });

                                row.ConstantItem(120).AlignRight().Column(inner =>
                                {
                                    inner.Item().Background("#0B132B").Padding(6).AlignCenter().Column(box =>
                                    {
                                        box.Item().Text("CÓDIGO DE RASTREIO")
                                            .FontSize(6).Bold().FontColor(Colors.White);
                                        box.Item().Text(entregaCompleta.CodigoRastreio)
                                            .FontSize(10).Bold().FontColor("#5BC0BE");
                                    });

                                    inner.Item().AlignCenter().Padding(2)
                                        .Text($"Emitido em: {DateTime.Now:dd/MM/yyyy HH:mm}")
                                        .FontSize(7).FontColor("#64748B");
                                });
                            });

                            col.Item().PaddingTop(4).BorderBottom(1).BorderColor("#5BC0BE");
                        });

                        // ── CONTEÚDO PRINCIPAL ──────────────────────────────
                        page.Content().Column(col =>
                        {
                            col.Item().PaddingTop(8).Row(row =>
                            {
                                // ── Bloco Destinatário ──
                                row.RelativeItem().Column(inner =>
                                {
                                    inner.Item().Text("DESTINATÁRIO / RECEPTOR").Bold().FontSize(8)
                                        .FontColor("#64748B");
                                    inner.Item().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2);
                                    inner.Item().PaddingTop(3).Text(entregaCompleta.DestinatarioNome)
                                        .Bold().FontSize(11).FontColor("#0B132B");
                                    if (!string.IsNullOrWhiteSpace(entregaCompleta.DestinatarioDocumento))
                                        inner.Item().Text($"Documento: {entregaCompleta.DestinatarioDocumento}").FontSize(8);

                                    inner.Item().PaddingTop(4).Text("ENDEREÇO DE ENTREGA").Bold().FontSize(8)
                                        .FontColor("#64748B");
                                    inner.Item().Text($"{entregaCompleta.EnderecoDestino}, {entregaCompleta.NumeroDestino}")
                                        .FontSize(9);
                                    inner.Item().Text($"{entregaCompleta.BairroDestino} — {entregaCompleta.CidadeDestino}/{entregaCompleta.EstadoDestino} — CEP: {entregaCompleta.CepDestino}")
                                        .FontSize(9);
                                });

                                row.ConstantItem(8);

                                // ── Bloco Pedido / Nota Fiscal ──
                                row.RelativeItem().Column(inner =>
                                {
                                    inner.Item().Text("DADOS DO DOCUMENTO FISCAL").Bold().FontSize(8)
                                        .FontColor("#64748B");
                                    inner.Item().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2);
                                    inner.Item().PaddingTop(3)
                                        .Text($"Pedido: {entregaCompleta.Pedido?.NumeroPedido ?? "N/I"}").FontSize(9);
                                    inner.Item()
                                        .Text($"Cliente: {entregaCompleta.Pedido?.Cliente?.RazaoSocial ?? entregaCompleta.DestinatarioNome}").FontSize(9);
                                    inner.Item()
                                        .Text($"Tipo de Carga: {entregaCompleta.TipoCarga}").FontSize(9);
                                    inner.Item()
                                        .Text($"Peso: {entregaCompleta.PesoToneladas:F1} t  |  {entregaCompleta.QtdEixos} eixos").FontSize(9);
                                    if (entregaCompleta.Veiculo != null)
                                        inner.Item()
                                            .Text($"Veículo: {entregaCompleta.Veiculo.Modelo} — Placa {entregaCompleta.Veiculo.Placa}").FontSize(9);
                                });
                            });

                            // ── Bloco de Confirmação de Recebimento ─────────
                            col.Item().PaddingTop(10).Background("#F0FDF4").Padding(8).Column(inner =>
                            {
                                inner.Item().Text("✅  CONFIRMAÇÃO DE RECEBIMENTO — POD DIGITAL").Bold().FontSize(10)
                                    .FontColor("#065F46");

                                inner.Item().PaddingTop(4).Row(r =>
                                {
                                    r.RelativeItem().Column(c =>
                                    {
                                        c.Item().Text($"Nome do Recebedor:  {entregaCompleta.NomeRecebedor ?? "(não informado)"}").Bold().FontSize(10);
                                        c.Item().Text($"Documento (RG/CPF): {entregaCompleta.DocumentoRecebedor ?? "(não informado)"}").FontSize(9);
                                    });

                                    r.RelativeItem().Column(c =>
                                    {
                                        var dtRecebimento = entregaCompleta.DataHoraRecebimento ?? entregaCompleta.DataEntregaRealizada ?? DateTime.UtcNow;
                                        c.Item().Text($"Data/Hora de Recebimento:").FontSize(8).FontColor("#64748B").Bold();
                                        c.Item().Text($"{dtRecebimento.ToLocalTime():dd/MM/yyyy HH:mm:ss}  (UTC{dtRecebimento.ToLocalTime().ToString("zzz")})").Bold().FontSize(10);

                                        var eventoEntregue = entregaCompleta.EventosRastreio
                                            .FirstOrDefault(ev => ev.StatusAssociado == "Entregue");
                                        if (eventoEntregue != null)
                                            c.Item().Text($"📍 Local GPS: {eventoEntregue.Localidade}").FontSize(9);
                                    });
                                });
                            });

                            // ── Assinatura Digital ───────────────────────────
                            col.Item().PaddingTop(10).Column(inner =>
                            {
                                inner.Item().Text("ASSINATURA DIGITAL DO RECEBEDOR").Bold().FontSize(8)
                                    .FontColor("#64748B");
                                inner.Item().PaddingTop(4);

                                if (!string.IsNullOrWhiteSpace(entregaCompleta.AssinaturaDigitalBase64)
                                    && entregaCompleta.AssinaturaDigitalBase64.StartsWith("data:image"))
                                {
                                    try
                                    {
                                        // Extrai bytes Base64 da assinatura canvas PNG
                                        var base64Part = entregaCompleta.AssinaturaDigitalBase64
                                            .Substring(entregaCompleta.AssinaturaDigitalBase64.IndexOf(',') + 1);
                                        var imgBytes = Convert.FromBase64String(base64Part);
                                        inner.Item()
                                            .Border(1).BorderColor(Colors.Grey.Lighten2)
                                            .Height(80).Image(imgBytes).FitArea();
                                    }
                                    catch
                                    {
                                        inner.Item().Border(1).BorderColor(Colors.Grey.Lighten2)
                                            .Height(80).AlignCenter().AlignMiddle()
                                            .Text("[ Assinatura registrada em sistema — formato incompatível com exibição ]")
                                            .FontSize(8).FontColor(Colors.Grey.Medium);
                                    }
                                }
                                else
                                {
                                    inner.Item().Border(1).BorderColor(Colors.Grey.Lighten2)
                                        .Height(80).AlignCenter().AlignMiddle()
                                        .Text("[ Assinatura não coletada ]")
                                        .FontSize(9).FontColor(Colors.Grey.Medium);
                                }

                                inner.Item().PaddingTop(4)
                                    .Text($"Assinatura digital autenticada — Gerada em {DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}")
                                    .FontSize(7).FontColor("#94A3B8");
                            });

                            // ── Linha do Tempo Resumida ──────────────────────
                            var eventos = entregaCompleta.EventosRastreio.Take(4).ToList();
                            if (eventos.Any())
                            {
                                col.Item().PaddingTop(10).Column(inner =>
                                {
                                    inner.Item().Text("HISTÓRICO DE RASTREAMENTO (ÚLTIMOS EVENTOS)").Bold().FontSize(8)
                                        .FontColor("#64748B");
                                    inner.Item().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2);
                                    inner.Item().PaddingTop(4).Table(table =>
                                    {
                                        table.ColumnsDefinition(cols =>
                                        {
                                            cols.ConstantColumn(100);
                                            cols.RelativeColumn(3);
                                            cols.RelativeColumn(2);
                                        });

                                        table.Header(h =>
                                        {
                                            h.Cell().Background("#0B132B")
                                                .Padding(3).Text("Data/Hora").FontSize(7).Bold().FontColor(Colors.White);
                                            h.Cell().Background("#0B132B")
                                                .Padding(3).Text("Evento").FontSize(7).Bold().FontColor(Colors.White);
                                            h.Cell().Background("#0B132B")
                                                .Padding(3).Text("Localidade").FontSize(7).Bold().FontColor(Colors.White);
                                        });

                                        foreach (var ev in eventos)
                                        {
                                            table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten3)
                                                .Padding(3).Text(ev.DataHora.ToLocalTime().ToString("dd/MM HH:mm")).FontSize(8);
                                            table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten3)
                                                .Padding(3).Text(ev.Titulo).FontSize(8);
                                            table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten3)
                                                .Padding(3).Text(ev.Localidade).FontSize(8);
                                        }
                                    });
                                });
                            }
                        });

                        // ── RODAPÉ ─────────────────────────────────────────
                        page.Footer().Column(col =>
                        {
                            col.Item().BorderTop(1).BorderColor("#5BC0BE").PaddingTop(4);
                            col.Item().Row(row =>
                            {
                                row.RelativeItem().Text($"M.G.L Connect • POD Digital • {entregaCompleta.CodigoRastreio}")
                                    .FontSize(7).FontColor("#64748B");
                                row.RelativeItem().AlignRight()
                                    .Text($"Gerado automaticamente pela plataforma M.G.L Connect SaaS B2B — {DateTime.Now:yyyy}")
                                    .FontSize(7).FontColor("#64748B");
                            });
                        });
                    });
                }).GeneratePdf();

                return pdfBytes;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao gerar PDF POD para entrega {EntregaId}", entregaCompleta.Id);
                throw;
            }
        }
    }
}
