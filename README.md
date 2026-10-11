# Forelia

Aplicação de conciliação financeira para comparar dados operacionais e contábeis importados de arquivos CSV, identificar divergências e acompanhar sua análise.

## Tecnologias previstas

- Backend: C# / .NET
- Banco de dados: PostgreSQL
- Frontend: React e TypeScript

## Funcionalidades planejadas

- Importação e validação de CSVs.
- Conciliação de valores e datas.
- Identificação de registros sem correspondência.
- Histórico de execuções e reprocessamento.
- Análise de divergências, indicadores e exportação em CSV.

## Estado atual

Backend com importação e validação de CSVs e persistência atômica no PostgreSQL. Conciliação ainda não implementada.

## Materiais do projeto

- [CSVs de teste](examples/csv/) — dados fictícios, incluindo erros intencionais para testar a validação.

## Executar localmente

Pré-requisitos: SDK .NET 10 e Docker em execução.

```sh
docker compose up -d --wait
dotnet tool restore
dotnet restore Forelia.sln
dotnet build Forelia.sln
ASPNETCORE_ENVIRONMENT=Development dotnet ef database update --project src/Forelia.Api
dotnet run --project src/Forelia.Api --launch-profile Forelia.Api
```

No Rider, abra `Forelia.sln` e execute o perfil `Forelia.Api`. As requisições de verificação estão em `src/Forelia.Api/Forelia.Api.http`.

- API: `http://localhost:5080`.
- Saúde da API: `/health/live`.
- Conexão com o banco: `/health/ready` (503 se indisponível).
- PostgreSQL local: `localhost:5433`, banco e usuário `forelia`. A senha de desenvolvimento é `forelia_local`; serve apenas para esse ambiente local.
- Dados do PostgreSQL são preservados no volume `postgres_data`. Use `docker compose stop` para parar o banco.

A conexão local está em `appsettings.Development.json`. Para outro ambiente, configure `ConnectionStrings__Forelia` por variável de ambiente.

As migrações são aplicadas explicitamente pelo comando acima, sem alteração automática do esquema na inicialização da API. A modelagem em `docs/` permanece referência.

O projeto usa .NET 10 LTS, com SDK 10.0.401 e EF Core/Npgsql da linha 10.

## Importar CSVs

```sh
curl -F 'arquivo=@examples/csv/01_base/operacional.csv' http://localhost:5080/api/importacoes/operacional
curl -F 'arquivo=@examples/csv/01_base/contabil.csv' http://localhost:5080/api/importacoes/contabil
```

Use a fonte `operacional` ou `contabil` e exatamente um arquivo no campo multipart `arquivo`. A resposta 201 contém o ID, metadados e a localização `GET /api/importacoes/{id}`. A consulta retorna somente metadados, sem carregar o CSV original.

Contrato aplicado às duas fontes:

- UTF-8 com ou sem BOM; separador `;`; cabeçalho obrigatório `id_operacao;data_operacao;valor`, nessa ordem. Aspas CSV e aspas escapadas são lidas pelo CsvHelper.
- Campos obrigatórios; data válida no formato `yyyy-MM-dd`; valor com ponto decimal, sem milhar ou notação científica, e no máximo duas casas decimais. Negativos e zero são aceitos. Valores fora da capacidade exata de `decimal` são rejeitados, sem arredondamento.
- Espaços externos são removidos e linhas em branco são ignoradas. Identificadores permanecem texto, preservando zeros, espaços internos e maiúsculas/minúsculas.
- Limite de 10 MB (10.485.760 bytes) por arquivo. Arquivo vazio ou somente com cabeçalho é rejeitado.
- Identificador repetido no mesmo arquivo rejeita a importação inteira, mesmo se valor ou data forem diferentes. A mensagem referencia a primeira linha e a linha repetida. Repetições entre arquivos diferentes não são descartadas, permitindo arquivos corrigidos.
- Qualquer erro rejeita o arquivo inteiro. Resposta 422 com `erros`, cada um contendo `linha`, `campo`, `codigo` e `mensagem`; nenhuma linha é incorporada.
- SHA-256 dos bytes originais e fonte identificam a importação. O mesmo conteúdo com outro nome retorna 409 e o ID da importação existente; outro conteúdo ou outra fonte permite nova importação. Uma tentativa rejeitada ou com falha técnica não bloqueia nova tentativa.

O CSV original (`bytea`), metadados e linhas válidas são salvos na mesma transação nas tabelas `arquivo_importado` e `registro_financeiro`. Valores usam `numeric` exato, protegido por uma restrição de até duas casas; datas usam `date`; instante de importação usa UTC. A aplicação não expõe atualização ou exclusão dos dados importados.

Uploads acima do limite retornam 413; fonte ou formulário inválido, 400; tipo de conteúdo diferente de multipart, 415. Falhas técnicas retornam mensagem genérica e detalhes ficam nos logs.

## Testes

```sh
docker compose up -d --wait
dotnet test Forelia.sln
```

Os testes usam os CSVs de `examples/csv/` e verificam normalização, erros intencionais, duplicidades, reimportação, limite de tamanho, concorrência e rollback após falha técnica. Os testes PostgreSQL criam e removem um banco isolado `forelia_test_*`, sem gravar dados no banco `forelia`. É necessário usuário com permissão para criar banco (o usuário do Docker local atende).

Para outra instância de teste, configure `FORELIA_TEST_POSTGRES` com a conexão administrativa. Para executar somente validação, sem PostgreSQL:

```sh
dotnet test Forelia.sln --filter 'Category!=Postgres'
```
