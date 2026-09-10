# Estabilização do MVP — 10/09/2026

## Correções

- Distribuição: manifesto vincula ambiente, URL, revisão, versão e SHA-256 do Setup. Não seleciona ZIP de outra publicação. Pacote incorreto/corrompido fica indisponível.
- Sessões: alteração de senha, desativação ou exclusão revoga acesso; perfil é recarregado antes da autorização.
- Credenciais locais: arquivo protegido por ACL; bandeja lê `agent.public.json`, sem tokens/chaves. Atualização conserva configurações existentes.
- Integridade S3: checksum validado pelo S3, leitura do arquivo sem concorrência de escrita, rejeição de mudança após manifesto e descarte correto do cliente S3.
- Caminhos: manifesto usa as pastas efetivas recebidas do painel e distingue raízes diferentes; impede colisões de arquivos homônimos.
- Serviço: respeita StateDir configurado. Console retorna erro para execução sem sucesso.
- Dependências: atualização de EF Core, SQLite, Hosting e testes para eliminar os alertas NuGet encontrados.
- Local: porta do Docker vinculada somente a 127.0.0.1.

## Automação

`scripts/ci/Test-System.ps1` executa restore, build, testes do painel e do Agent e auditoria de dependências. Falha se houver testes quebrados ou dependências vulneráveis. `-IncludeAws -AwsTestBucket <bucket-de-teste>` adiciona teste real do uploader, SHA-256 e deduplicação; utiliza a cadeia local de credenciais AWS, nunca credenciais no painel. Objetos sintéticos ficam em prefixo UUID sob `trat-validation/`, sem exclusão.

GitHub Actions executa os testes em pull requests e antes de publicação. Depois de publicar a imagem, cria a branch `deploy-hml` ou `deploy-production` contendo somente o compose com digest imutável. Configure a stack Portainer para essa branch, caminho `deploy/portainer/compose.yml`, GitOps polling habilitado. Assim uma alteração de Git só dispara implantação depois de a imagem existir. A stack conserva variáveis, rede e volumes.

O job de deploy aguarda `/api/v1/readiness`: banco acessível, pacote íntegro e revisão correta. Produção requer `HML_APPROVED_REVISION` igual ao SHA aprovado após teste completo do Agent em hml e a mesma revisão ainda disponível em hml. Atualize a branch production por fast-forward do SHA aprovado; não gerar merge SHA diferente. A variável é um registro operacional de aprovação, não substitui evidências.

`Validar-TRAT.ps1` verifica login, painel, logo, manifesto e SHA do Setup baixado; ZIP é opcional (`-IncludeZip`).

## Evidências e limites

Baseline: 16 testes existentes. Após correções: 26 testes do painel e 5 testes do Agent aprovados; 1 teste real AWS aprovado. Build sem avisos/erros e auditoria NuGet sem vulnerabilidades reportadas após atualização.

Diagnóstico inicial: local/hml saudáveis; produção HTTP 502. Bucket de homologação sem versionamento/Object Lock. Teste de upload não comprova backup imutável nem restauração ponta a ponta.

A revisão independente do Codex Security foi interrompida pelo limite de uso dos trabalhadores antes de concluir cobertura. Os achados recebidos foram investigados e corrigidos, mas não se afirma auditoria exaustiva concluída.

Validação empacotada, visual, serviço Windows, redeploy e produção serão registrados após execução. Não declarar MVP liberado sem esses gates.

## Reversão

Reverter o commit da branch de deploy para o digest anterior; preservar volumes, banco e chaves. Não usar `down --volumes`. Alterações de caminhos com múltiplas raízes escrevem em novas chaves `root-<id>/...`; objetos antigos não são apagados.

Referências: [checksum PutObject](https://docs.aws.amazon.com/AmazonS3/latest/API/API_PutObject.html), [proteção de ambientes GitHub](https://docs.github.com/en/actions/concepts/workflows-and-actions/deployment-environments).
