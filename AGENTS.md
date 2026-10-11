# Orientações para trabalhar na Forelia

## Contexto e fontes

- Leia o `README.md` para conhecer o projeto e `docs/DESENVOLVIMENTO.md` para consultar execução, testes e contratos da API.
- Consulte `docs/FORELIA_REQUISITOS_V1.md`, quando disponível. RN01–RN18 estão aprovadas, mas critérios ainda indefinidos precisam de decisão explícita antes de implementação.
- A modelagem em `docs/` é uma proposta de referência. Não a trate como esquema obrigatório.
- As decisões de importação, conciliação, análise, filtros e dashboard estão consolidadas na seção “Decisões de implementação” de `docs/DESENVOLVIMENTO.md`; não as reabra apenas porque os documentos iniciais ainda as apresentam como pendentes.
- Ao fechar novos critérios com o usuário, atualize esse registro e o contrato correspondente no mesmo guia, mantendo o README simples.

## Implementação

- Mantenha a solução simples: C#/.NET, PostgreSQL e Entity Framework Core, seguindo a organização existente.
- Não adicione camadas, padrões, bibliotecas ou serviços sem necessidade concreta.
- Não crie documentos adicionais de planejamento sem solicitação.
- Preserve a atomicidade das importações, os arquivos originais e a precisão dos valores financeiros. Correções chegam por novas importações.
- Divergências financeiras e registros sem correspondência são resultados de negócio; não são falhas técnicas.
- Alterar a situação de análise nunca muda o resultado financeiro. Observações são imutáveis; correções devem ser novas observações. Identificadores de analistas nesta V1 são identidades não autenticadas.
- Não presuma chave de correspondência, cardinalidade ou tolerância de valor/data. Apresente a decisão necessária antes de implementar esses comportamentos.
- Faça alterações de esquema com migrações do EF Core. Versione migrações, arquivos Designer e o ModelSnapshot.
- Nunca inclua segredos reais ou dados financeiros reais nos exemplos e testes.

## Verificação e entrega

- Use os exemplos válidos e os erros intencionais de `examples/csv/` para verificar comportamentos relevantes.
- Execute `dotnet build Forelia.sln` e os testes pertinentes à alteração.
- Para a suíte completa: PostgreSQL disponível via `docker compose up -d --wait`, seguido de `dotnet test Forelia.sln`.
- Sem PostgreSQL, execute `dotnet test Forelia.sln --filter 'Category!=Postgres'` e informe que os testes de integração não foram executados.
- Os testes de integração devem usar bancos isolados `forelia_test_*`, sem alterar o banco local `forelia`.
- Preserve alterações existentes do usuário. Não faça commit ou push sem solicitação.
- Após cada implementação relevante, forneça os comandos de `git add` e uma sugestão de `git commit` para o usuário executar.
- Responda em português, explicando o que mudou, como foi verificado e quais decisões ou limitações permanecem.
- Considere o Rider como IDE do usuário e mantenha a solução e as requisições `.http` utilizáveis nele.
