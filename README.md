# TRAT

## Execucao local simples

Para testar em outro servidor sem publicar na internet:

1. Na maquina de origem, gere os artifacts locais do painel e do agent.
2. Zipe a pasta inteira `TRAT`.
3. Copie e extraia a pasta no servidor de teste.
4. Execute `Iniciar-TRAT.ps1` na raiz do projeto extraido.

O script raiz:

- reutiliza o pacote local do painel em `artifacts\trat-local\TRAT.ControlPlane.Local`;
- se esse pacote ainda nao existir e houver `dotnet SDK`, tenta gera-lo automaticamente;
- sobe o painel local e abre o navegador em `http://localhost:5080`.

Para encerrar o painel local, execute `Parar-TRAT.ps1`.
