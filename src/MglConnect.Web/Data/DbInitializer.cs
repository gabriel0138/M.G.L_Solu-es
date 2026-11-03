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
                RazaoSocial = "Empresa de Demonstração 1 Ltda",
                NomeFantasia = "Cliente Demo 1",
                Cnpj = "00.000.000/0001-00",
                Email = "cliente1@example.invalid",
                Telefone = "00000000000",
                Cep = "00000-000",
                Logradouro = "Endereço de demonstração",
                Numero = "0",
                Complemento = "Sem complemento",
                Bairro = "Demonstração",
                Cidade = "Cidade Demo",
                Estado = "ZZ",
                Ativo = true,
                DataCadastro = DateTime.UtcNow.AddDays(-30)
            };

            var cliente2 = new Cliente
            {
                RazaoSocial = "Empresa de Demonstração 2 Ltda",
                NomeFantasia = "Cliente Demo 2",
                Cnpj = "00.000.000/0002-00",
                Email = "cliente2@example.invalid",
                Telefone = "00000000000",
                Cep = "00000-000",
                Logradouro = "Endereço de demonstração",
                Numero = "0",
                Complemento = "Sem complemento",
                Bairro = "Demonstração",
                Cidade = "Cidade Demo",
                Estado = "ZZ",
                Ativo = true,
                DataCadastro = DateTime.UtcNow.AddDays(-20)
            };

            context.Clientes.AddRange(cliente1, cliente2);
            await context.SaveChangesAsync();

            // 3. Criar Usuários (Admin, Motorista e Perfil Cliente)
            var admin = new Usuario
            {
                Nome = "Administrador de Demonstração",
                Login = "admin",
                SenhaHash = hasher.HashPassword(demoCredentials.AdminPassword),
                Perfil = "Admin",
                Telefone = "00000000000",
                Cpf = "000.000.000-00",
                Ativo = true,
                DataCadastro = DateTime.UtcNow
            };

            var motorista1 = new Usuario
            {
                Nome = "Motorista de Demonstração",
                Login = "carlos.motorista",
                SenhaHash = hasher.HashPassword(demoCredentials.DriverPassword),
                Perfil = "Motorista",
                Telefone = "00000000000",
                Cpf = "000.000.000-00",
                Ativo = true,
                DataCadastro = DateTime.UtcNow
            };

            var usuarioCliente = new Usuario
            {
                Nome = "Usuário Cliente de Demonstração",
                Login = "cliente.demo",
                SenhaHash = hasher.HashPassword(demoCredentials.ClientPassword),
                Perfil = "Cliente",
                ClienteId = cliente1.Id,
                Telefone = "00000000000",
                Cpf = "000.000.000-00",
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
                Status = "Concluido"
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
                AtribuicaoAtiva = true,
                DestinatarioNome = "Destinatário de Demonstração 1",
                DestinatarioDocumento = "DOC-DEMO-1",
                EnderecoDestino = "Endereço de demonstração",
                NumeroDestino = "0",
                ComplementoDestino = "Sem complemento",
                BairroDestino = "Demonstração",
                CidadeDestino = "Cidade Demo",
                EstadoDestino = "ZZ",
                CepDestino = "00000-000",
                StatusEntrega = "Pendente",
                DataCriacao = DateTime.UtcNow.AddDays(-1),
                DataPrevisaoEntrega = DateTime.UtcNow.AddDays(1),
                Observacoes = "Dados sintéticos para demonstração.",
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
                DestinatarioNome = "Destinatário de Demonstração 2",
                DestinatarioDocumento = "DOC-DEMO-2",
                EnderecoDestino = "Endereço de demonstração",
                NumeroDestino = "0",
                ComplementoDestino = "Sem complemento",
                BairroDestino = "Demonstração",
                CidadeDestino = "Cidade Demo",
                EstadoDestino = "ZZ",
                CepDestino = "00000-000",
                StatusEntrega = "Entregue",
                DataCriacao = DateTime.UtcNow.AddDays(-3),
                DataPrevisaoEntrega = DateTime.UtcNow,
                DataEntregaRealizada = DateTime.UtcNow.AddHours(-2),
                Observacoes = "Dados sintéticos para demonstração.",
                AlturaMetros = veiculo3.AlturaMetros,
                LarguraMetros = veiculo3.LarguraMetros,
                PesoToneladas = veiculo3.PesoToneladas,
                QtdEixos = veiculo3.QtdEixos,
                TipoCarga = veiculo3.TipoCarga,
                RotaRestritaCaminhao = true,
                // Comprovante Digital de Entrega (POD)
                NomeRecebedor = "Recebedor de Demonstração",
                DocumentoRecebedor = "DOC-DEMO-RECEBEDOR",
                DataHoraRecebimento = DateTime.UtcNow.AddHours(-2),
                AssinaturaDigitalBase64 = "data:image/svg+xml;utf8,<svg xmlns='http://www.w3.org/2000/svg' width='200' height='60'><path d='M10 40 Q 60 10, 110 35 T 190 25' stroke='%230B132B' stroke-width='2' fill='none'/><text x='15' y='55' font-family='sans-serif' font-size='10' fill='%23666'>Recebedor Demo</text></svg>"
            };

            context.Entregas.AddRange(entrega1, entrega2);
            await context.SaveChangesAsync();

            // 6. Linha do Tempo (Checkpoints)
            var eventosEntrega1 = new[]
            {
                new EventoRastreio
                {
                    EntregaId = entrega1.Id,
                    DataHora = entrega1.DataCriacao,
                    Titulo = "Pedido validado e remessa preparada",
                    Descricao = "Aguardando início de turno autenticado pelo motorista.",
                    Localidade = "Cajamar - SP",
                    StatusAssociado = "Pendente",
                    Icone = "bi-box-seam"
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
                    Descricao = "Motorista de demonstração a caminho do destino.",
                    Localidade = "Santos - SP",
                    StatusAssociado = "Em transporte",
                    Icone = "bi-truck"
                },
                new EventoRastreio
                {
                    EntregaId = entrega2.Id,
                    DataHora = DateTime.UtcNow.AddHours(-2),
                    Titulo = "Comprovante Digital de Entrega (POD) Confirmado",
                    Descricao = "Pacote recebido por destinatário de demonstração. Assinatura sintética arquivada.",
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
