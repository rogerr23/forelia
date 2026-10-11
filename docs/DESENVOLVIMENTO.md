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

Os testes de análise verificam mudanças de situação e reabertura, preservação financeira, observações após encerramento, limites de entrada, consultas paginadas, gravações simultâneas, bloqueio de edição/exclusão no banco e migração de dados existentes.

Os testes dos filtros verificam igualdade exata de identificadores, combinação com E, datas de referência, limites inclusivos dos períodos, situação atual da análise, contagem antes da paginação, entradas inválidas e isolamento entre execuções.

Para outra instância de teste, configure `FORELIA_TEST_POSTGRES` com a conexão administrativa. Para executar somente os testes sem PostgreSQL:

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

As requisições em `Forelia.Api.http` capturam automaticamente os IDs das importações (incluindo respostas 409) para executar e consultar a conciliação no Rider. Dashboard, exportação e frontend permanecem para as próximas etapas.

## Análise de divergências e observações

Itens `DIVERGENTE`, `SEM_REGISTRO_CONTABIL` e `SEM_OPERACAO_FINANCEIRA` começam com `situacaoAnalise: PENDENTE`. Itens `CONCILIADO` retornam `situacaoAnalise: null` e não aceitam análise ou observações (409).

A situação pode mudar livremente entre `PENDENTE`, `EM_ANALISE` e `ANALISADA`, inclusive reabrir uma análise. Repetir a mesma situação é permitido. Essa alteração não muda valores, datas, divergências, resultado financeiro ou totais da execução. Cada nova conciliação começa com sua própria análise; reutilizar uma execução concluída preserva a situação e as observações existentes.

```http
PATCH /api/conciliacoes/{conciliacaoId}/itens/{itemId}/analise
Content-Type: application/json

{"situacao": "EM_ANALISE"}
```

Para adicionar uma observação:

```http
POST /api/conciliacoes/{conciliacaoId}/itens/{itemId}/observacoes
Content-Type: application/json

{"texto": "Investigar diferença de valor e data.", "analista": "analista-teste"}
```

Texto e analista são obrigatórios, com limites de 2.000 e 100 caracteres, respectivamente. Campos em branco, acima do limite ou com caracteres nulos retornam 400. Espaços externos são removidos após a validação do limite. Data/hora é atribuída pelo servidor em UTC.

A resposta 201 contém ID, item, texto, analista, data/hora, `identidadeAutenticada: false` e o aviso **Identidade não autenticada**. O identificador é informado pelo usuário, sem verificação de identidade nesta V1.

Observações podem ser adicionadas em qualquer situação da análise, inclusive `ANALISADA`. Cada chamada cria uma nova observação. Correções devem ser feitas por uma nova observação; não há endpoints de edição ou exclusão. Um trigger no PostgreSQL também rejeita UPDATE e DELETE de observações.

Consultas:

- `GET /api/conciliacoes/{conciliacaoId}/itens/{itemId}`: item com sua situação atual.
- `GET /api/conciliacoes/{conciliacaoId}/itens/{itemId}/observacoes?pagina=1&tamanhoPagina=20`: observações da mais antiga para a mais recente, com desempate pelo ID. Mesmos limites de paginação das demais consultas.
- `GET /api/conciliacoes/{conciliacaoId}/itens/{itemId}/observacoes/{observacaoId}`: uma observação.

IDs inexistentes ou pertencentes a outra execução/item retornam 404. Execuções não concluídas não disponibilizam análise ou observações (409). A migração inicializa a análise das divergências existentes como `PENDENTE`, preservando os resultados anteriores.

## Filtros de consulta

Use os parâmetros opcionais em `GET /api/conciliacoes/{id}/itens`:

| Parâmetro | Critério |
|---|---|
| `idOperacao` | Igualdade exata, sensível a maiúsculas/minúsculas, preservando zeros e espaços. Não faz busca parcial. |
| `resultado` | `CONCILIADO`, `DIVERGENTE`, `SEM_REGISTRO_CONTABIL` ou `SEM_OPERACAO_FINANCEIRA`. |
| `situacaoAnalise` | `PENDENTE`, `EM_ANALISE` ou `ANALISADA`. Itens conciliados não têm situação de análise e não correspondem a esse filtro. |
| `dataOperacao` | Data exata de referência, em `yyyy-MM-dd`. |
| `dataInicial` | Data de referência maior ou igual ao limite informado, em `yyyy-MM-dd`. |
| `dataFinal` | Data de referência menor ou igual ao limite informado, em `yyyy-MM-dd`. |

A data de referência é a **data operacional**; quando não existir registro operacional, usar a data contábil. Em um par com datas diferentes, a data contábil não substitui a operacional.

Todos os filtros informados são combinados com **E**. Uma data exata pode ser combinada com um período; se os critérios válidos não tiverem correspondência, a resposta será 200, com `total: 0` e `itens: []`.

Exemplos:

```http
GET /api/conciliacoes/{id}/itens?resultado=DIVERGENTE&situacaoAnalise=PENDENTE
GET /api/conciliacoes/{id}/itens?idOperacao=OP004&dataOperacao=2026-10-04
GET /api/conciliacoes/{id}/itens?dataInicial=2026-10-03&dataFinal=2026-10-05&pagina=1&tamanhoPagina=20
```

Os filtros são aplicados no PostgreSQL antes da contagem e da paginação. `total` conta somente os itens filtrados; os totais financeiros da execução permanecem iguais. A ordenação continua por identificador, com desempate pelo ID. Página inicial 1, tamanho padrão 20 e máximo 100.

Para deixar um filtro desativado, omita o parâmetro. Parâmetros vazios, identificador em branco ou com caractere nulo, valores fora das listas, datas inválidas e período invertido retornam 400 com `codigo: FILTRO_INVALIDO`. Resultado e situação devem usar os valores em maiúsculas apresentados na tabela. Os filtros abrangem somente os itens da execução indicada e não disponibilizam resultados de execuções não concluídas.

## Decisões de implementação

Registro consolidado dos critérios confirmados nas etapas já implementadas. As seções deste guia detalham os contratos correspondentes. RN01–RN18 estão aprovadas, mas isso não aprova automaticamente os detalhes ainda não definidos de etapas futuras. A modelagem inicial em `docs/` continua sendo uma referência.

| Tema | Decisão adotada |
|---|---|
| Base técnica | Backend C#/.NET 10, PostgreSQL e EF Core; manter implementação simples, seguindo a estrutura existente. React/TypeScript está previsto para o frontend. |
| Documentação | README resumido; execução, testes, API e este registro ficam neste guia. Não criar documentos adicionais de planejamento sem solicitação. |
| Layout CSV — RN03 | `csv-v1`: UTF-8, BOM opcional, separador `;`, cabeçalho obrigatório `id_operacao;data_operacao;valor`. Datas `yyyy-MM-dd`, decimal com ponto, sem milhar/notação científica e até duas casas. |
| Normalização — RN03 | Remover espaços externos, ignorar linhas em branco e preservar identificadores como texto, incluindo zeros, maiúsculas/minúsculas e espaços internos. |
| Valores e limites da importação | Negativos e zero aceitos, sem arredondamento financeiro; rejeitar valores fora da representação exata de `decimal`. Limite por arquivo: 10.485.760 bytes. |
| Duplicidade — RN01 | Identificador repetido dentro do arquivo rejeita a importação inteira, inclusive repetição exata. Referenciar primeira linha e linha repetida. Repetição entre arquivos distintos permite correções. |
| Identidade do arquivo — RN02 | SHA-256 dos bytes originais por fonte. Renomear conteúdo idêntico não permite reimportação. Conteúdo diferente ou outra fonte permite importação; falhas não bloqueiam nova tentativa. |
| Integridade — RN17/RN18 | Rejeitar todo o arquivo se houver linha inválida. Persistir original, metadados e registros na mesma transação. Preservar dados de origem; corrigir por novo arquivo. Falhas técnicas não produzem resultados definitivos parciais. |
| Correspondência — RN04 | Um arquivo de cada fonte por execução, relação um para um por `id_operacao`, com igualdade sensível a maiúsculas/minúsculas e preservação de zeros. |
| Comparações — RN05/RN06/RN07 | Valores e datas exatamente iguais, sem tolerância. Preservar divergência de valor e data no mesmo par e identificar ausências nas duas direções. |
| Execuções — RN08/RN09/RN12 | Reutilizar execução concluída com os mesmos arquivos e regras. Correção gera outra execução. Falha permite nova tentativa com outro ID e preservação da anterior. Processamento síncrono, versão fixa `conciliacao-v1` e serialização de solicitações equivalentes. |
| Indicadores — RN14 | Cada par ou registro sem correspondência é um item. Par divergente conta uma vez, mesmo com duas divergências. Taxa = pares conciliados / itens × 100, duas casas, ponto médio para longe de zero; denominador zero retorna zero. |
| Análise — RN10 | Divergências e ausências começam `PENDENTE`; conciliados ficam sem situação. Permitir transições entre as três situações, inclusive reabertura, sem alterar o resultado financeiro. |
| Observações — RN11 | Texto obrigatório até 2.000 caracteres e analista obrigatório até 100; instante UTC do servidor. Identidade não autenticada. Permitir novas observações inclusive após `ANALISADA`; nunca editar ou excluir observações anteriores. |
| Filtros — RN13 | Combinação com E, identificador exato, resultado/situação existentes, prioridade da data operacional com fallback contábil, período inclusivo e data exata combinável com período. Filtros inválidos retornam 400; nenhuma correspondência retorna lista vazia. |
| Paginação — RN13 | Aplicar filtros antes de contar e paginar. Página a partir de 1, padrão 20 e máximo 100. Itens por identificador; histórico de execuções do mais recente para o mais antigo; observações da mais antiga para a mais recente, sempre com desempate por ID. |

Ao aprovar novos critérios, atualizar este registro e a seção do contrato afetado. Detalhes ainda em aberto do dashboard, da exportação e de outras etapas devem ser apresentados ao usuário antes de implementação.

