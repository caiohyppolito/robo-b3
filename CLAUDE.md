# Robô B3: guia para agentes

- Backend .NET 10, Clean Architecture: `Domain` (puro, sem I/O) ← `Application` (casos de uso + portas) ← `Infrastructure` (EF Core/SQL Server, HTTP) ← `Api` / `Workers`.
- Pacotes NuGet com versão central em `Directory.Packages.props`; warnings são erros (`Directory.Build.props`).
- Regras de risco ficam em `RoboB3.Domain/Risk/RiskManager.cs` e precisam de teste para qualquer mudança.
- Ordens que só reduzem posição nunca podem ser bloqueadas. O robô nunca se destrava sozinho.
- Mudou o modelo EF? Gere migração: `dotnet ef migrations add <Nome> --project src/RoboB3.Infrastructure --output-dir Persistence/Migrations`.
- Nunca versionar segredos: usar `user-secrets` ou variáveis de ambiente.
- Textos para o usuário, logs e nomes de testes em português; identificadores de código em inglês.
- Verificar: `dotnet build && dotnet test` e `cd web && npm run build`.
