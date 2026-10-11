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
- Testar API no navegador: `http://localhost:5080/swagger`.
- Prévia visual do dashboard: `http://localhost:5080/dashboard`.
- Saúde da API: `/health/live`.
- Conexão com o banco: `/health/ready` (503 se indisponível).
- PostgreSQL local: `localhost:5433`, banco e usuário `forelia`. A senha de desenvolvimento é `forelia_local`; serve apenas para esse ambiente local.
- Dados do PostgreSQL são preservados no volume `postgres_data`. Use `docker compose stop` para parar o banco.

A conexão local está em `appsettings.Development.json`. Para outro ambiente, configure `ConnectionStrings__Forelia` por variável de ambiente.

As migrações são aplicadas explicitamente pelo comando acima, sem alteração automática do esquema na inicialização da API. A modelagem em `docs/` permanece referência.

O projeto usa .NET 10 LTS, com SDK 10.0.401 e EF Core/Npgsql da linha 10.

## Testar pelo Swagger e visualizar o dashboard

Execute o perfil `Forelia.Api` no Rider e abra `http://localhost:5080/swagger`. O perfil já usa `Development`. O OpenAPI é gerado pelo ASP.NET Core e o Swagger UI é servido pelo backend, incluindo seus arquivos JavaScript/CSS, sem CDN.

1. Em **Importação**, use `POST /api/importacoes/{fonte}` com `fonte: operacional` e selecione `examples/csv/01_base/operacional.csv` no campo `arquivo`. Execute e copie o `id` retornado. Se já foi importado, a resposta 409 fornece `importacaoExistenteId`; reutilize esse ID.
2. Repita com `fonte: contabil` e `examples/csv/01_base/contabil.csv`.
3. Em **Conciliação**, use `POST /api/conciliacoes`, preenchendo `arquivoOperacionalId` e `arquivoContabilId` com os IDs das duas importações. Copie o ID da conciliação retornada. A resposta pode ser 201 para nova execução ou 200 para resultado reutilizado.
4. Consulte o resumo e os itens usando esse ID. Em consultas, preencha somente os filtros desejados; omita os demais. Valores de resultado/situação usam maiúsculas e datas usam `yyyy-MM-dd`.
5. Para análise e observações, copie também o ID de um item divergente da consulta de itens; os endpoints usam `conciliacaoId` e `itemId`.
6. Em **Relatórios**, consulte `GET /api/conciliacoes/{id}/exportacao`, com os filtros desejados. A resposta oferece o CSV para download. Não há paginação na exportação.

Abra `http://localhost:5080/dashboard` para visualizar os indicadores e as barras de ocorrências. A página consulta a API real, permite selecionar uma conciliação concluída entre as dez tentativas recentes ou informar o ID de outra execução. Use **Atualizar** após importar/conciliar pelo Swagger. Também é possível abrir `/dashboard?conciliacaoId=ID` diretamente. Ausência de resultados e erros de consulta são apresentados na página.

O cenário base mostra 9 itens, 4 pares conciliados e taxa de 44,44%. Para testar uma correção, importe `examples/csv/02_correcao/contabil_corrigido.csv` e concilie com a importação operacional original. Essa execução terá 8 itens e taxa de 100%; a anterior continua consultável.

Swagger (`/swagger`), documento OpenAPI (`/openapi/v1.json`) e prévia HTML (`/dashboard`) existem somente em `Development`. A prévia é uma ferramenta simples de verificação servida pelo backend, sem React ou processo separado de frontend. As APIs de negócio permanecem disponíveis nos demais ambientes.

## Frontend — React/TypeScript

O frontend está em `frontend/`, no mesmo repositório. Abra a raiz `forelia/` no VS Code para acessar também a documentação e incluir tudo no mesmo commit, mantendo `Forelia.sln` no Rider para a API. Pré-requisito: Node.js 22.12 ou superior da linha 22, ou uma versão LTS posterior compatível com Vite; o ambiente verificado usa Node.js 22.14.0 e npm 10.9.2.

Com a API em execução no Rider, use um segundo terminal:

```sh
npm --prefix frontend ci
npm --prefix frontend run dev
```

Abra `http://127.0.0.1:5173`. O servidor Vite encaminha `/api` para `http://localhost:5080`; o backend não precisa de configuração CORS para esse fluxo. Para outra porta da API, copie `frontend/.env.example` para `frontend/.env.local` e ajuste `FORELIA_API_URL`. Reinicie o Vite após alterar essa configuração. A porta 5173 é fixa: se estiver ocupada, encerre o outro processo antes de iniciar.

Verificações do frontend:

```sh
npm --prefix frontend run build
npm --prefix frontend run lint
npm --prefix frontend test
```

O build gera `frontend/dist/`. Publicação e hospedagem não foram configuradas; em produção, disponibilize os arquivos estáticos e encaminhe `/api` para o backend na mesma origem. `npm run dev` é apenas para desenvolvimento local.

### Organização dos arquivos

`frontend/src/` está organizado por responsabilidade:

```text
src/
  App.tsx                   # Seleção da tela pela rota local
  main.tsx                  # Inicialização do React
  api/
    client.ts               # HTTP, importação, exportação e leitura decimal exata
    types.ts                # Contratos de dados da API
    client.test.ts
  components/               # Layout e componentes reutilizados entre telas
  pages/
    DashboardPage.tsx
    HistoryPage.tsx
    ImportPage.tsx
    results/
      ResultsPage.tsx
      ItemDetails.tsx       # Análise e observações, exclusivas dos resultados
  hooks/
    useRemote.ts            # Consulta com cancelamento e estados de carregamento
  utils/                    # Formatação, filtros e seus testes
  styles/                   # Estilos globais e da aplicação
```

Componentes usados em mais de uma tela ficam em `components/`; os exclusivos de uma tela ficam ao lado dela. Testes ficam próximos do código verificado. As configurações de Vite, TypeScript e npm continuam na raiz de `frontend/`. A separação não adiciona camadas de domínio ou bibliotecas.

### Interface e integração

A base visual escolhida em 11/10/2026 usa navegação superior, fundo claro e grafite um pouco mais escuro nos textos. Os números dos indicadores e as barras de ocorrências usam o azul da proposta revisada. A pedido do usuário, a revisão seguinte acrescenta destaque azul às ações principais e à navegação selecionada, cores e ícones nas etiquetas de situação e alertas, além de respostas visuais ao passar o mouse e clicar. A folha da marca foi removida; o protótipo propõe duas setas opostas para representar a comparação entre fontes. As demais telas serão refinadas a partir dessa base.

A interface implementa a base visual aprovada e usa a API real, sem dados simulados na aplicação. O protótipo da conversa permanece uma referência de layout. As rotas locais usam fragmentos (`#visao-geral`, `#nova-conciliacao`, `#historico` e `#resultados/ID`), permitindo recarregar e usar voltar/avançar sem configuração de rotas no servidor.

Em **Nova conciliação**, selecione um CSV de cada fonte. **Validar e importar arquivos** envia cada fonte ao seu endpoint; erros são mostrados por linha, e respostas 409 reutilizam os metadados da importação indicada. Cada arquivo tem sua própria transação: se uma fonte for rejeitada, uma importação válida da outra fonte permanece disponível. **Executar conciliação** usa os IDs recebidos e abre os resultados. Botões ficam indisponíveis durante as gravações, evitando envios repetidos na mesma tela; nenhuma gravação recebe retentativa automática.

O dashboard seleciona a execução concluída mais recente por padrão, permite escolher execuções concluídas recentes e conserva o recorte aprovado. O histórico tem paginação e permite abrir qualquer execução. Tentativas não concluídas exibem a situação, sem resultados parciais.

Nos resultados, preencha os seis filtros e use **Aplicar filtros**. A lista é paginada; a exportação usa os filtros aplicados e inclui todas as páginas. Selecionar um item abre os detalhes, a situação da análise e as observações paginadas. A identidade do analista é informada a cada observação, com aviso de identidade não autenticada; não há edição ou exclusão de observações.

Valores financeiros são lidos com `lossless-json` e mantidos como texto decimal no frontend, para preservar a precisão do `decimal` recebido da API. A exibição usa vírgula e duas casas, sem conversão para `Number`, cálculo ou arredondamento financeiro e sem atribuir uma moeda não definida. Datas de operação são exibidas sem conversão de fuso; instantes UTC usam o fuso local do navegador. Falhas de consulta possuem estados de erro e ações para atualizar; falhas técnicas de gravação não são apresentadas como divergências financeiras.

## Testes

```sh
docker compose up -d --wait
dotnet test Forelia.sln
```

Os testes usam os CSVs de `examples/csv/` e verificam normalização, erros intencionais, duplicidades, reimportação, limite de tamanho e rollback após falha técnica. A conciliação é verificada com cenários base, corrigido e normalizado, múltiplas divergências, precisão financeira, concorrência, reutilização de execuções, histórico, paginação e recuperação de tentativas interrompidas. Os testes PostgreSQL criam e removem um banco isolado `forelia_test_*`, sem gravar dados no banco `forelia`. É necessário usuário com permissão para criar banco (o usuário do Docker local atende).

Os testes de análise verificam mudanças de situação e reabertura, preservação financeira, observações após encerramento, limites de entrada, consultas paginadas, gravações simultâneas, bloqueio de edição/exclusão no banco e migração de dados existentes.

Os testes dos filtros verificam igualdade exata de identificadores, combinação com E, datas de referência, limites inclusivos dos períodos, situação atual da análise, contagem antes da paginação, entradas inválidas e isolamento entre execuções.

Os testes do dashboard verificam banco vazio, cenário base, correção sem soma entre execuções, reutilização, seleção de resultados históricos, contagem de tentativas por situação, limite de dez execuções recentes, resultados indisponíveis e preservação dos indicadores após mudanças na análise.

Os testes da exportação verificam cabeçalho, BOM, delimitador, valores exatos, ausências, divergências múltiplas, todos os filtros sem paginação, situação atual da análise, separação entre original e correção, caracteres especiais CSV e rejeição de resultados indisponíveis. As ferramentas de desenvolvimento têm testes sem banco para verificar o documento OpenAPI, upload multipart, interface Swagger e indisponibilidade das páginas fora de Development.

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

As requisições em `Forelia.Api.http` capturam automaticamente os IDs das importações (incluindo respostas 409) para executar e consultar a conciliação, o dashboard e a exportação no Rider. O frontend React/TypeScript usa os mesmos contratos.

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

## Dashboard — API

```http
GET /api/dashboard
GET /api/dashboard?conciliacaoId=ID_DA_CONCILIACAO
```

Sem parâmetro, selecionar a conciliação `CONCLUIDA` mais recente pela data de criação, com desempate pelo ID em ordem crescente, como no histórico. Uma tentativa mais recente que esteja pendente, processando ou com falha não substitui o resultado concluído. O parâmetro permite consultar uma conciliação concluída anterior. ID inexistente retorna 404, ID malformado retorna 400 e execução não concluída retorna 409 com `codigo: RESULTADO_INDISPONIVEL`.

A resposta contém:

| Campo | Conteúdo |
|---|---|
| `execucoes` | `total`, `pendentes`, `processando`, `concluidas` e `falhas` de todo o histórico. Cada tentativa persistida conta uma vez; reutilizar uma execução não cria outra tentativa. |
| `conciliacaoSelecionada` | Resumo da execução escolhida, ou `null` quando não existir uma execução concluída. |
| `indicadores` | Registros de origem, itens, pares conciliados/divergentes, ausências nas duas fontes, `itensComDivergencia` e taxa da execução escolhida. |
| `distribuicaoDivergencias` | Quatro categorias fixas, cada uma com `tipo` e `ocorrencias`: `VALOR`, `DATA`, `SEM_REGISTRO_CONTABIL` e `SEM_OPERACAO_FINANCEIRA`. |
| `execucoesRecentes` | Até dez tentativas de todo o histórico, incluindo falhas, ordenadas pela criação mais recente, com desempate pelo ID crescente. |

Os indicadores e a distribuição pertencem **somente à execução selecionada**. Não há soma de operações entre execuções, evitando duplicar operações reprocessadas. `itensComDivergencia` inclui pares divergentes e registros sem correspondência, contando cada item uma vez. A distribuição conta ocorrências: um par com diferença de valor e data contribui para ambas as categorias. Mudanças de análise não alteram esses indicadores.

No cenário base: 16 registros de origem, 9 itens, 4 pares conciliados, 3 pares divergentes, uma ausência em cada fonte e 5 itens com divergência. A distribuição tem 2 ocorrências de valor, 2 de data e uma de cada ausência: 6 ocorrências, com taxa de 44,44%. A correção apresenta 8 itens conciliados e taxa de 100%, sem somar os 9 itens da execução anterior.

Sem execução concluída, os indicadores e as quatro categorias retornam zero; as contagens de tentativas e o histórico continuam disponíveis. A seleção do dashboard não filtra esses dois campos globais. A API não oferece filtros por período. A prévia em `/dashboard`, somente em Development, distingue os cartões de itens das barras de ocorrências; o frontend React/TypeScript utiliza esses mesmos contratos.

## Exportação CSV — RN16

```http
GET /api/conciliacoes/{id}/exportacao
GET /api/conciliacoes/{id}/exportacao?resultado=DIVERGENTE&situacaoAnalise=PENDENTE
```

Somente execuções `CONCLUIDA` podem ser exportadas. O relatório aceita os mesmos seis filtros da consulta de itens, com os mesmos critérios de validação, combinação com E e datas. Exporta **todos os itens filtrados**, sem paginação, ordenados por identificador e depois ID. `pagina` e `tamanhoPagina` não fazem parte deste contrato e não limitam o relatório.

Download com nome `conciliacao-{id}.csv`, tipo `text/csv; charset=utf-8`, UTF-8 com BOM, separador `;` e finais de linha CRLF. Aspas, delimitadores e quebras de linha dentro dos campos recebem o escape CSV padrão. Identificadores são preservados como texto, sem remoção de zeros ou alteração do conteúdo. Ao importar o CSV em uma planilha, configure a coluna de identificador como texto para preservar essa representação.

Cabeçalho fixo:

```text
id_operacao;valor_operacional;valor_contabil;data_operacional;data_contabil;resultado;divergencias;situacao_analise
```

Valores com ponto e exatamente duas casas, sem arredondar os dados financeiros; datas `yyyy-MM-dd`. Valores e datas da fonte ausente são campos vazios. `divergencias` contém `VALOR`, `DATA`, `VALOR|DATA` ou vazio; as ausências são representadas no `resultado`. `situacao_analise` reflete a situação atual no momento da leitura e fica vazia nos itens conciliados. Observações e identificação do analista não fazem parte deste relatório.

Sem correspondências, retornar 200 e somente o cabeçalho com BOM. Filtro inválido retorna 400 com `FILTRO_INVALIDO`, execução inexistente 404 e não concluída 409 com `RESULTADO_INDISPONIVEL`, sem download. A exportação lê os registros progressivamente e não altera resultados ou observações. PDF e XLSX continuam fora desta etapa.

## Decisões de implementação

Registro consolidado dos critérios confirmados nas etapas já implementadas. As seções deste guia detalham os contratos correspondentes. RN01–RN18 estão aprovadas, mas isso não aprova automaticamente os detalhes ainda não definidos de etapas futuras. A modelagem inicial em `docs/` continua sendo uma referência.

| Tema | Decisão adotada |
|---|---|
| Base técnica | Backend C#/.NET 10, PostgreSQL e EF Core; manter implementação simples, seguindo a estrutura existente. Frontend React/TypeScript com Vite em `frontend/`. |
| Organização e execução do frontend | Aprovado em 11/10/2026: `frontend/` no mesmo repositório, aberto no VS Code; API e solução .NET no Rider. React/TypeScript com Vite e npm, CSS próprio e ícones Lucide. Navegação por fragmentos e proxy local `/api`, sem alteração dos contratos do backend. `lossless-json` preserva os tokens decimais financeiros como texto; nenhuma tolerância, moeda ou cálculo financeiro é introduzido no frontend. |
| Layout do frontend | Escolhido em 11/10/2026: navegação superior, fundo claro, grafite mais escuro nos textos e azul nas métricas. Revisão solicitada: botões, alertas e etiquetas com mais destaque visual e remoção da folha da marca. O protótipo propõe ações principais azuis, estados com cor e ícone e marca com duas setas opostas. Layout aprovado para implementação; o protótipo usa dados fictícios e o frontend React/TypeScript consome a API real. |
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
| Recorte do dashboard — RN15 | Autorizado pelo usuário em 11/10/2026 para implementação e revisão posterior. Indicadores e distribuição de uma única execução concluída, selecionada por ID ou pela criação mais recente, com desempate por ID crescente. Não somar operações entre execuções. |
| Histórico do dashboard — RN15 | Contagens globais de tentativas por situação e dez tentativas mais recentes, incluindo falhas. Reutilização não aumenta a contagem. Selecionar outra conciliação não filtra esses campos. |
| Distribuição do dashboard — RN14/RN15 | Contar ocorrências de valor, data e ausências separadamente. Total de itens com divergência inclui ausências e conta cada item uma vez; uma divergência dupla produz duas ocorrências. Preservar indicadores ao alterar a análise. |
| Limites do dashboard — RN15 | Sem resultado concluído: seleção nula e indicadores zerados. ID inexistente 404, malformado 400 e não concluído 409. API e testes na primeira etapa; prévia visual local adicionada na etapa seguinte. Frontend React/TypeScript implementado na etapa seguinte; filtros por período do dashboard continuam futuros. |
| Exportação — RN16 | Aprovado em 11/10/2026: uma execução concluída, mesmos seis filtros, todos os itens filtrados sem paginação e mesma ordenação. UTF-8 com BOM, `;`, CRLF, datas `yyyy-MM-dd`, valores com duas casas e campos ausentes vazios. Oito colunas definidas no contrato; múltiplas divergências `VALOR|DATA`, análise atual, sem observações. Sem itens: somente cabeçalho; erros 400/404/409. |
| Testes interativos e prévia visual | Solicitação de 11/10/2026: Swagger UI com OpenAPI gerado pelo ASP.NET Core e prévia HTML simples em `/dashboard`, servidos somente em Development. Prévia reutiliza a API e os critérios aprovados do dashboard, com indicadores e barras de ocorrências; frontend React implementado em processo separado; filtros por período do dashboard continuam futuros. |

Ao aprovar novos critérios, atualizar este registro e a seção do contrato afetado. As decisões podem ser revistas com o usuário a partir deste registro. Detalhes ainda em aberto dos filtros por período do dashboard e de outras etapas devem ser apresentados ao usuário antes de implementação.

