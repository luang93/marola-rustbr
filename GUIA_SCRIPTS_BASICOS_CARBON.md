# Guia basico para criar scripts no seu servidor Carbon

Este documento foi feito para este ambiente sem alterar nada do servidor.

## Onde os scripts ficam

Neste servidor, os plugins/scripts do Carbon ficam em:

```text
/home/rustserver/rustserver/carbon/plugins
```

Os plugins existentes nessa pasta usam o padrao Oxide/Carbon em C#.

## O que ja esta ativo neste servidor

Pelo arquivo de configuracao do Carbon, este servidor ja esta com os watchers de script ativos.

Isso significa que, na pratica, ao salvar um arquivo `.cs` em `carbon/plugins`, o Carbon normalmente detecta a mudanca e tenta carregar ou recarregar o plugin automaticamente.

Se precisar fazer manualmente, os aliases configurados neste servidor sao:

```text
load   -> c.load
reload -> c.reload
unload -> c.unload
```

Exemplos de uso no console do servidor ou RCON:

```text
c.load MeuPrimeiroPlugin
c.reload MeuPrimeiroPlugin
c.unload MeuPrimeiroPlugin
```

## Estrutura minima de um plugin

Crie um arquivo novo em `carbon/plugins` com o nome do plugin.

Exemplo:

```text
/home/rustserver/rustserver/carbon/plugins/MeuPrimeiroPlugin.cs
```

Conteudo minimo:

```csharp
using Oxide.Core.Libraries.Covalence;

namespace Oxide.Plugins
{
    [Info("MeuPrimeiroPlugin", "SeuNome", "1.0.0")]
    [Description("Exemplo basico para Carbon/Rust")]
    public class MeuPrimeiroPlugin : CovalencePlugin
    {
        private void Init()
        {
            Puts("MeuPrimeiroPlugin carregado.");
        }

        private void Unload()
        {
            Puts("MeuPrimeiroPlugin descarregado.");
        }
    }
}
```

## Exemplo util: comando simples no chat/console

Se quiser um plugin realmente basico para testar, use este:

```csharp
using Oxide.Core.Libraries.Covalence;

namespace Oxide.Plugins
{
    [Info("MeuPrimeiroPlugin", "SeuNome", "1.0.0")]
    [Description("Plugin basico com comando de teste")]
    public class MeuPrimeiroPlugin : CovalencePlugin
    {
        private void Init()
        {
            Puts("Plugin iniciado com sucesso.");
        }

        [Command("testeplugin")]
        private void TestePluginCommand(IPlayer player, string command, string[] args)
        {
            player.Reply("Plugin funcionando normalmente.");
        }
    }
}
```

Depois de salvar, rode no jogo ou no console:

```text
/testeplugin
```

## Quando usar `CovalencePlugin` ou `RustPlugin`

Use `CovalencePlugin` quando quiser comandos e APIs mais genericas.

Use `RustPlugin` quando precisar trabalhar mais diretamente com tipos especificos do Rust, como `BasePlayer`, entidades do mapa, inventario, spawn, damage hooks e coisas do tipo.

Para comecar, `CovalencePlugin` costuma ser o caminho mais simples.

## Hooks basicos mais comuns

Alguns metodos que voce vai usar bastante:

```csharp
private void Init()
{
}

private void OnServerInitialized()
{
}

private void Unload()
{
}
```

Resumo rapido:

- `Init()`: roda quando o plugin carrega.
- `OnServerInitialized()`: roda quando o servidor ja terminou de subir.
- `Unload()`: roda quando o plugin e descarregado.

## Permissoes basicas

Se quiser restringir um comando para admins ou grupos especificos:

```csharp
using Oxide.Core.Libraries.Covalence;

namespace Oxide.Plugins
{
    [Info("PermissaoExemplo", "SeuNome", "1.0.0")]
    [Description("Exemplo com permissao")]
    public class PermissaoExemplo : CovalencePlugin
    {
        private const string UsePermission = "permissaoexemplo.use";

        private void Init()
        {
            permission.RegisterPermission(UsePermission, this);
        }

        [Command("vipteste")]
        private void VipTeste(IPlayer player, string command, string[] args)
        {
            if (!player.HasPermission(UsePermission))
            {
                player.Reply("Voce nao tem permissao para usar este comando.");
                return;
            }

            player.Reply("Permissao validada com sucesso.");
        }
    }
}
```

Para conceder permissao:

```text
oxide.grant user STEAM_ID permissaoexemplo.use
oxide.grant group admin permissaoexemplo.use
```

## Arquivos que um plugin costuma usar

Neste servidor, normalmente voce vai lidar com:

- `carbon/plugins`: codigo do plugin `.cs`
- `carbon/configs`: configuracoes geradas pelos plugins
- `carbon/data`: dados salvos pelos plugins
- `carbon/lang`: textos e traducoes

Nem todo plugin usa tudo isso, mas esse e o fluxo normal.

## Fluxo seguro para criar scripts sem baguncar o servidor

1. Crie um plugin novo com nome unico.
2. Comece com um comando simples de teste.
3. Salve em `carbon/plugins`.
4. Veja o console/log para confirmar se compilou.
5. Teste o comando no servidor.
6. So depois disso adicione hooks mais complexos.

Uma boa pratica e nao editar plugins grandes ja instalados no inicio. Crie um plugin pequeno separado para aprender e validar o fluxo.

## Erros comuns

### 1. Nome do arquivo diferente do nome da classe

Evite isso. Mantenha o nome do arquivo e o nome da classe alinhados.

Exemplo correto:

- Arquivo: `MeuPrimeiroPlugin.cs`
- Classe: `public class MeuPrimeiroPlugin : CovalencePlugin`

### 2. Namespace errado

Use este namespace:

```csharp
namespace Oxide.Plugins
```

### 3. Falta de referencias basicas

Para plugins simples com comando, isto costuma bastar:

```csharp
using Oxide.Core.Libraries.Covalence;
```

### 4. Tentar fazer tudo de uma vez

Primeiro faca carregar.
Depois faca responder comando.
Depois adicione permissao.
Depois mexa com hooks do jogo.

## Modelo rapido para copiar

```csharp
using Oxide.Core.Libraries.Covalence;

namespace Oxide.Plugins
{
    [Info("PluginModelo", "SeuNome", "1.0.0")]
    [Description("Modelo inicial para novos plugins")]
    public class PluginModelo : CovalencePlugin
    {
        private const string UsePermission = "pluginmodelo.use";

        private void Init()
        {
            permission.RegisterPermission(UsePermission, this);
            Puts("PluginModelo carregado.");
        }

        [Command("pluginmodelo")]
        private void PluginModeloCommand(IPlayer player, string command, string[] args)
        {
            if (!player.HasPermission(UsePermission))
            {
                player.Reply("Sem permissao.");
                return;
            }

            player.Reply("Tudo certo. O plugin esta funcionando.");
        }

        private void Unload()
        {
            Puts("PluginModelo descarregado.");
        }
    }
}
```

## Referencia rapida deste servidor

- Pasta principal do servidor: `/home/rustserver/rustserver`
- Pasta dos plugins Carbon: `/home/rustserver/rustserver/carbon/plugins`
- Watchers de script: ativos
- Comandos manuais disponiveis: `c.load`, `c.reload`, `c.unload`

## Sugestao pratica de primeiro teste

Se voce esta comecando agora, faca exatamente isto:

1. Crie `MeuPrimeiroPlugin.cs` em `carbon/plugins`.
2. Cole o exemplo do comando `testeplugin`.
3. Salve o arquivo.
4. Veja se o Carbon carregou sem erro.
5. Rode `/testeplugin` no servidor.

Se isso funcionar, o ambiente de desenvolvimento do seu servidor ja esta pronto para plugins basicos.