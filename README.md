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
- Análise de divergências e observações imutáveis dos analistas.
- Filtros de consulta por operação, resultado, datas e situação da análise.
- API do dashboard com indicadores por conciliação e execuções recentes.

Tela do dashboard, exportação e frontend estão previstos para as próximas etapas.

## Desenvolvimento

- [Guia de execução, testes, API e decisões de implementação](docs/DESENVOLVIMENTO.md).
- [CSVs de exemplo](examples/csv/) com dados fictícios e erros intencionais.
- [Requisições para executar no Rider](src/Forelia.Api/Forelia.Api.http).
