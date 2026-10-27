using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MglConnect.Web.Models;
using MglConnect.Web.Services;

namespace MglConnect.Web.Data
{
    internal sealed record DemoSeedCredentials(
        string AdminPassword,
        string DriverPassword,
        string ClientPassword);

    public static class DbInitializer
    {
        internal static async Task InitializeAsync(
            MglDbContext context,
            IPasswordHasher hasher,
            DemoSeedCredentials? demoCredentials)
        {
            await context.Database.EnsureCreatedAsync();
            await DatabaseSchemaUpdater.EnsurePedidoRouteColumnsAsync(context);
            await DatabaseSchemaUpdater.EnsureDriverProfileColumnsAsync(context);
            await DatabaseSchemaUpdater.EnsureOperationalSecuritySchemaAsync(context);

            if (demoCredentials is null)
            {
                return;
            }

            if (await context.Usuarios.AnyAsync()
                || await context.Empresas.AnyAsync()
                || await context.Clientes.AnyAsync()
                || await context.Pedidos.AnyAsync()
                || await context.Entregas.AnyAsync()
                || await context.EventosRastreio.AnyAsync()
                || await context.Veiculos.AnyAsync())
            {
                return;
            }

            // 1. Criar Frota de Veículos Pesados (Heavy Vehicle Fleet)
            var veiculo1 = new Veiculo
            {
                Placa = "MGL-8E19",
                Modelo = "Scania R450 6x2 Highline",
                CapacidadeCargaKg = 32000m,
                AlturaMetros = 4.20m,
                LarguraMetros = 2.60m,
                ComprimentoMetros = 18.60m,
                PesoToneladas = 29.50m,
                QtdEixos = 6,
                TipoCarga = "Carga Geral Seca",
                Ativo = true
            };

            var veiculo2 = new Veiculo
            {
                Placa = "LOG-4H22",
                Modelo = "Volvo FH 540 6x4 Globetrotter",
                CapacidadeCargaKg = 45000m,
                AlturaMetros = 4.35m,
                LarguraMetros = 2.60m,
                ComprimentoMetros = 19.80m,
                PesoToneladas = 42.00m,
                QtdEixos = 7,
                TipoCarga = "Carga Pesada Indivisível",
                Ativo = true
            };

            var veiculo3 = new Veiculo
            {
                Placa = "ROD-7K33",
                Modelo = "Mercedes-Benz Actros 2651",
                CapacidadeCargaKg = 27000m,
                AlturaMetros = 3.95m,
                LarguraMetros = 2.55m,
                ComprimentoMetros = 16.50m,
                PesoToneladas = 23.00m,
                QtdEixos = 5,
                TipoCarga = "Produtos Perigosos",
                Ativo = true
            };

            context.Veiculos.AddRange(veiculo1, veiculo2, veiculo3);
            await context.SaveChangesAsync();

            // 2. Criar Clientes Corporativos
            var cliente1 = new Cliente
            {
                RazaoSocial = "Alpha Distribuidora de Eletrônicos Ltda",
                NomeFantasia = "Alpha Tech Logística",
                Cnpj = "12.345.678/0001-90",
                Email = "operacional@alphatech.com.br",
                Telefone = "(13) 3219-8800",
                Cep = "11013-001",
                Logradouro = "Rua do Comércio",
                Numero = "150",
                Complemento = "Sala 402",
                Bairro = "Centro",
                Cidade = "Santos",
                Estado = "SP",
                Ativo = true,
                DataCadastro = DateTime.UtcNow.AddDays(-30)
            };

            var cliente2 = new Cliente
            {
                RazaoSocial = "Varejo Express Mercadorias e E-commerce S.A.",
                NomeFantasia = "Express Hub",
                Cnpj = "98.765.432/0001-11",
                Email = "contato@expresshub.com.br",
                Telefone = "(11) 3100-2244",
                Cep = "01310-100",
                Logradouro = "Avenida Paulista",
                Numero = "1200",
                Complemento = "Andar 10",
                Bairro = "Bela Vista",
                Cidade = "São Paulo",
                Estado = "SP",
                Ativo = true,
                DataCadastro = DateTime.UtcNow.AddDays(-20)
            };

            context.Clientes.AddRange(cliente1, cliente2);
            await context.SaveChangesAsync();

            // 3. Criar Usuários (Admin, Motorista e Perfil Cliente)
            var admin = new Usuario
            {
                Nome = "Administrador Geral MGL",
                Login = "admin",
                SenhaHash = hasher.HashPassword(demoCredentials.AdminPassword),
                Perfil = "Admin",
                Telefone = "(13) 99111-2233",
                Cpf = "111.222.333-44",
                Ativo = true,
                DataCadastro = DateTime.UtcNow
            };

            var motorista1 = new Usuario
            {
                Nome = "Carlos Eduardo Santos",
                Login = "carlos.motorista",
                SenhaHash = hasher.HashPassword(demoCredentials.DriverPassword),
                Perfil = "Motorista",
                Telefone = "(13) 98844-5566",
                Cpf = "222.333.444-55",
                DataNascimento = new DateTime(1988, 4, 15),
                Ativo = true,
                DataCadastro = DateTime.UtcNow
            };

            var usuarioCliente = new Usuario
            {
                Nome = "Gerente de Logística - Alpha Tech",
                Login = "cliente.demo",
                SenhaHash = hasher.HashPassword(demoCredentials.ClientPassword),
                Perfil = "Cliente",
                ClienteId = cliente1.Id,
                Telefone = "(13) 99888-7711",
                Cpf = "444.555.666-77",
                Ativo = true,
                DataCadastro = DateTime.UtcNow
            };

            context.Usuarios.AddRange(admin, motorista1, usuarioCliente);
            await context.SaveChangesAsync();

            // 4. Criar Pedidos
            var pedido1 = new Pedido
            {
                NumeroPedido = "PED-2026-0001",
                ClienteId = cliente1.Id,
                Descricao = "Lote de 50x Roteadores Wi-Fi 6 Corporativos Gigabit",
                ValorTotal = 14500.00m,
                PesoKg = 25.5m,
                DataPedido = DateTime.UtcNow.AddDays(-2),
                Status = "ComEntregaAtribuida"
            };

            var pedido2 = new Pedido
            {
                NumeroPedido = "PED-2026-0002",
                ClienteId = cliente2.Id,
                Descricao = "Caixa com 120x Smartwatches Esportivos e Fones TWS",
                ValorTotal = 8900.00m,
                PesoKg = 14.2m,
                DataPedido = DateTime.UtcNow.AddDays(-4),
                Status = "ComEntregaAtribuida"
            };

            var pedido3 = new Pedido
            {
                NumeroPedido = "PED-2026-0003",
                ClienteId = cliente1.Id,
                Descricao = "Servidor Rack 1U 32GB RAM e Discos NVMe",
                ValorTotal = 22800.00m,
                PesoKg = 18.0m,
                DataPedido = DateTime.UtcNow.AddHours(-5),
                Status = "Registrado"
            };

            context.Pedidos.AddRange(pedido1, pedido2, pedido3);
            await context.SaveChangesAsync();

            // 5. Criar Entregas com Roteamento Pesado e POD
            var entrega1 = new Entrega
            {
                PedidoId = pedido1.Id,
                MotoristaId = motorista1.Id,
                VeiculoId = veiculo1.Id,
                CodigoRastreio = "MGL-BR100201",
                DestinatarioNome = "MegaByte Informática & Serviços",
                DestinatarioDocumento = "44.555.666/0001-77",
                EnderecoDestino = "Avenida Ana Costa",
                NumeroDestino = "200",
                ComplementoDestino = "Loja 12 - Térreo",
                BairroDestino = "Gonzaga",
                CidadeDestino = "Santos",
                EstadoDestino = "SP",
                CepDestino = "11060-000",
                StatusEntrega = "Em transporte",
                DataCriacao = DateTime.UtcNow.AddDays(-1),
                DataPrevisaoEntrega = DateTime.UtcNow.AddDays(1),
                Observacoes = "Horário comercial de recebimento: 09h às 18h.",
                AlturaMetros = veiculo1.AlturaMetros,
                LarguraMetros = veiculo1.LarguraMetros,
                PesoToneladas = veiculo1.PesoToneladas,
                QtdEixos = veiculo1.QtdEixos,
                TipoCarga = veiculo1.TipoCarga,
                RotaRestritaCaminhao = true
            };

            var entrega2 = new Entrega
            {
                PedidoId = pedido2.Id,
                MotoristaId = motorista1.Id,
                VeiculoId = veiculo3.Id,
                CodigoRastreio = "MGL-BR200302",
                DestinatarioNome = "Supermercado Litoral Sul Ltda",
                DestinatarioDocumento = "18.999.888/0001-22",
                EnderecoDestino = "Avenida Presidente Wilson",
                NumeroDestino = "450",
                ComplementoDestino = "Docas 2",
                BairroDestino = "José Menino",
                CidadeDestino = "Santos",
                EstadoDestino = "SP",
                CepDestino = "11065-200",
                StatusEntrega = "Entregue",
                DataCriacao = DateTime.UtcNow.AddDays(-3),
                DataPrevisaoEntrega = DateTime.UtcNow,
                DataEntregaRealizada = DateTime.UtcNow.AddHours(-2),
                Observacoes = "Entregue diretamente na recepção das docas.",
                AlturaMetros = veiculo3.AlturaMetros,
                LarguraMetros = veiculo3.LarguraMetros,
                PesoToneladas = veiculo3.PesoToneladas,
                QtdEixos = veiculo3.QtdEixos,
                TipoCarga = veiculo3.TipoCarga,
                RotaRestritaCaminhao = true,
                // Comprovante Digital de Entrega (POD)
                NomeRecebedor = "Maria Oliveira Santos",
                DocumentoRecebedor = "34.871.992-X",
                DataHoraRecebimento = DateTime.UtcNow.AddHours(-2),
                AssinaturaDigitalBase64 = "data:image/svg+xml;utf8,<svg xmlns='http://www.w3.org/2000/svg' width='200' height='60'><path d='M10 40 Q 60 10, 110 35 T 190 25' stroke='%230B132B' stroke-width='2' fill='none'/><text x='15' y='55' font-family='sans-serif' font-size='10' fill='%23666'>Maria O. Santos</text></svg>"
            };

            context.Entregas.AddRange(entrega1, entrega2);
            await context.SaveChangesAsync();

            // 6. Linha do Tempo (Checkpoints)
            var eventosEntrega1 = new[]
            {
                new EventoRastreio
                {
                    EntregaId = entrega1.Id,
                    DataHora = DateTime.UtcNow.AddDays(-1).AddHours(2),
                    Titulo = "Pedido postado e bipado no Centro de Distribuição",
                    Descricao = "Mercadoria recebida e conferida no CD Logístico MGL Cajamar.",
                    Localidade = "Cajamar - SP",
                    StatusAssociado = "Pendente",
                    Icone = "bi-box-seam"
                },
                new EventoRastreio
                {
                    EntregaId = entrega1.Id,
                    DataHora = DateTime.UtcNow.AddDays(-1).AddHours(8),
                    Titulo = "Em transferência entre centros operacionais",
                    Descricao = "Carga despachada em caminhão pesado Scania R450 rumo ao centro de distribuição litorâneo via Rodoanel.",
                    Localidade = "São Paulo - SP",
                    StatusAssociado = "Em transporte",
                    Icone = "bi-truck"
                },
                new EventoRastreio
                {
                    EntregaId = entrega1.Id,
                    DataHora = DateTime.UtcNow.AddHours(-4),
                    Titulo = "Chegou à unidade de distribuição local",
                    Descricao = "Pacote descarregado e triado para a rota do dia.",
                    Localidade = "Santos - SP",
                    StatusAssociado = "Em transporte",
                    Icone = "bi-geo-alt-fill"
                },
                new EventoRastreio
                {
                    EntregaId = entrega1.Id,
                    DataHora = DateTime.UtcNow.AddHours(-1),
                    Titulo = "Saiu para entrega ao destinatário",
                    Descricao = "Motorista Carlos Eduardo iniciou o percurso final de entrega.",
                    Localidade = "Santos - SP",
                    StatusAssociado = "Em transporte",
                    Icone = "bi-bicycle"
                }
            };

            var eventosEntrega2 = new[]
            {
                new EventoRastreio
                {
                    EntregaId = entrega2.Id,
                    DataHora = DateTime.UtcNow.AddDays(-3),
                    Titulo = "Pedido recebido e etiqueta emitida",
                    Descricao = "Lote recebido no CD Logístico Cajamar.",
                    Localidade = "Cajamar - SP",
                    StatusAssociado = "Pendente",
                    Icone = "bi-box-seam"
                },
                new EventoRastreio
                {
                    EntregaId = entrega2.Id,
                    DataHora = DateTime.UtcNow.AddDays(-2),
                    Titulo = "Em trânsito para a unidade local",
                    Descricao = "Transporte rodoviário pesado em andamento.",
                    Localidade = "São Paulo - SP",
                    StatusAssociado = "Em transporte",
                    Icone = "bi-truck"
                },
                new EventoRastreio
                {
                    EntregaId = entrega2.Id,
                    DataHora = DateTime.UtcNow.AddHours(-6),
                    Titulo = "Saiu para entrega",
                    Descricao = "Motorista Carlos Eduardo a caminho do endereço de entrega.",
                    Localidade = "Santos - SP",
                    StatusAssociado = "Em transporte",
                    Icone = "bi-truck"
                },
                new EventoRastreio
                {
                    EntregaId = entrega2.Id,
                    DataHora = DateTime.UtcNow.AddHours(-2),
                    Titulo = "Comprovante Digital de Entrega (POD) Confirmado",
                    Descricao = "Pacote recebido e conferido por Maria Oliveira Santos (Doc: 34.871.992-X). Assinatura digital arquivada.",
                    Localidade = "Santos - SP",
                    StatusAssociado = "Entregue",
                    Icone = "bi-patch-check-fill"
                }
            };

            context.EventosRastreio.AddRange(eventosEntrega1);
            context.EventosRastreio.AddRange(eventosEntrega2);
            await context.SaveChangesAsync();
        }
    }
}
