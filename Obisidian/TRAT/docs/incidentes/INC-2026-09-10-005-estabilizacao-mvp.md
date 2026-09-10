# INC-2026-09-10-005 — Estabilização e validação do MVP

Estado: investigação e correção em andamento. Data: 10/09/2026.

## Evidências iniciais

- Baseline: 16 testes do ControlPlane aprovados; não havia projeto de testes do Agent.
- Local saudável; homologação saudável na revisão 48f8cc74aed25966abc407a281af6323a4dfaacd. Produção retorna HTTP 502.
- Catálogo escolhe Setup e ZIP independentemente por mtime, sem vincular revisão nem hash. A versão depende dos metadados PE, e não do manifesto distribuído.
- Pipeline publica imagem, mas não executa redeploy nem verifica a revisão efetivamente servida. O validador exige ZIP embora o pipeline distribua apenas Setup.
- Sessão do painel mantém autorização em cache após alteração/desativação/exclusão do usuário.
- Bucket dedicado de homologação acessível por STS/S3; versionamento não habilitado e Object Lock ausente. Isso não comprova imutabilidade para produção.
- Upload compara metadado SHA fornecido pelo próprio cliente, sem solicitar que S3 valide esse SHA; arquivo pode mudar entre scan e upload.

## Tratamento

Vincular manifesto, revisão e SHA-256 ao Setup; impedir mistura entre ambientes; testar regressões; validar sessão contra estado persistido; verificar checksum no serviço S3; automatizar validação e promoção com gates.

Preservar bancos, volumes, configurações e alterações de terceiros. Testes AWS usam somente conteúdo sintético em prefixo único, sem exclusões. Produção depende da validação em homologação.

## Validação e reversão

Resultados serão registrados ao executar os testes. Reversão por imagem imutável anterior, preservando volumes e backups; nenhuma remoção de volumes.
