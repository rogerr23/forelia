# Guia de desenvolvimento

Execute os comandos deste guia na raiz do repositório.

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

## Testes

```sh
docker compose up -d --wait
dotnet test Forelia.sln
```

Os testes usam os CSVs de `examples/csv/` e verificam normalização, erros intencionais, duplicidades, reimportação, limite de tamanho e rollback após falha técnica. A conciliação é verificada com cenários base, corrigido e normalizado, múltiplas divergências, precisão financeira, concorrência, reutilização de execuções, histórico, paginação e recuperação de tentativas interrompidas. Os testes PostgreSQL criam e removem um banco isolado `forelia_test_*`, sem gravar dados no banco `forelia`. É necessário usuário com permissão para criar banco (o usuário do Docker local atende).

Para outra instância de teste, configure `FORELIA_TEST_POSTGRES` com a conexão administrativa. Para executar somente validação, sem PostgreSQL:

```sh
dotnet test Forelia.sln --filter 'Category!=Postgres'
```

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

## Executar conciliação

Escolha os IDs de duas importações existentes, uma operacional e outra contábil:

```http
POST /api/conciliacoes
Content-Type: application/json

{
  "arquivoOperacionalId": "ID_DA_IMPORTACAO_OPERACIONAL",
  "arquivoContabilId": "ID_DA_IMPORTACAO_CONTABIL"
}
```

Critérios da versão `conciliacao-v1`:

- Uma importação operacional e uma contábil por execução. A correspondência é um para um pelo `id_operacao`, com comparação sensível a maiúsculas/minúsculas e preservação dos zeros. A importação já rejeita identificadores repetidos dentro do arquivo.
- Valores e datas devem ser exatamente iguais, sem tolerância ou arredondamento de valores financeiros. A ordem das linhas não interfere.
- Resultados: `CONCILIADO`, `DIVERGENTE`, `SEM_REGISTRO_CONTABIL` e `SEM_OPERACAO_FINANCEIRA`. Um par divergente pode apresentar `VALOR`, `DATA` ou ambos, sem duplicação do item.
- A taxa é pares conciliados divididos pelo total de itens, vezes 100, arredondada para duas casas com ponto médio para longe de zero. Cada par ou registro sem correspondente conta como um item. Denominador zero produz taxa zero.
- Nova execução retorna 201. Mesmos arquivos e mesma versão das regras após conclusão retornam 200 com a execução existente. Arquivo corrigido permite nova execução e preserva a anterior. A versão das regras é fixa nesta etapa, sem configuração de tolerâncias pela API.
- Processamento síncrono nesta etapa, com estados `PENDENTE`, `PROCESSANDO`, `CONCLUIDA` e `FALHOU`. Divergências não impedem a conclusão. Solicitações simultâneas equivalentes são serializadas por um lock do PostgreSQL e reutilizam a execução concluída.
- Resultados e totais são persistidos na mesma transação. Falha técnica retorna 500, preserva a tentativa como `FALHOU` quando o banco permite e não disponibiliza resultados parciais. Nova tentativa recebe outro ID. Detalhes técnicos ficam nos logs.
- Se uma solicitação for interrompida e deixar estado ativo, uma nova solicitação equivalente registra a tentativa anterior como `FALHOU`/`PROCESSAMENTO_INTERROMPIDO` antes de tentar novamente. Essa recuperação ocorre sob o mesmo lock, sem tarefa automática em segundo plano.

Consultas disponíveis:

- `GET /api/conciliacoes/{id}`: estado, arquivos, versão das regras e totais.
- `GET /api/conciliacoes?pagina=1&tamanhoPagina=20`: histórico, mais recentes primeiro.
- `GET /api/conciliacoes/{id}/itens?pagina=1&tamanhoPagina=20`: itens definitivos, ordenados por identificador. Execução ainda não concluída retorna 409.

A paginação começa em 1; tamanho padrão 20 e máximo 100. Arquivo inexistente retorna 404; fontes trocadas ou paginação inválida retornam 400.

O cenário `01_base` produz 9 itens: 4 pares conciliados, 3 pares divergentes e 2 registros sem correspondência, com taxa de 44,44%. Com `02_correcao/contabil_corrigido.csv`, os 8 pares ficam conciliados, com taxa de 100%.

As requisições em `Forelia.Api.http` capturam automaticamente os IDs das importações (incluindo respostas 409) para executar e consultar a conciliação no Rider. Análise manual, observações, filtros de negócio, dashboard e exportação permanecem para as próximas etapas.

