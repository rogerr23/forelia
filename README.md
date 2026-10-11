# Forelia

Aplicação de conciliação financeira que compara dados operacionais e contábeis importados de CSVs, identifica divergências e preserva o histórico das execuções.

## Tecnologias

- Backend: C# / .NET 10.
- Banco de dados: PostgreSQL.
- Frontend previsto: React e TypeScript.

## Estado atual

- Importação e validação de CSVs.
- Conciliação de valores e datas, com identificação de registros sem correspondência.
- Histórico de execuções e consultas paginadas.

Análise manual de divergências, observações, dashboard e exportação estão previstos para as próximas etapas.

## Desenvolvimento

- [Guia de execução, testes e API](docs/DESENVOLVIMENTO.md).
- [CSVs de exemplo](examples/csv/) com dados fictícios e erros intencionais.
- [Requisições para executar no Rider](src/Forelia.Api/Forelia.Api.http).
