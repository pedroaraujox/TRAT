# Contribuindo com o TRAT

## Fluxo

1. trabalhe a partir de `development`;
2. mantenha a alteração pequena e rastreável;
3. atualize testes e documentação;
4. valide o artefato real;
5. revise `git diff` e `git status`;
6. envie para revisão antes de qualquer promoção futura.

## Ambiente

Consulte [docs/COMO-RODAR-LOCALMENTE.md](docs/COMO-RODAR-LOCALMENTE.md).

## Padrões

- C# com nullable habilitado e warnings como erros;
- scripts compatíveis com PowerShell 5.1 quando destinados ao Agent/Windows legado;
- nenhuma capacidade de exclusão no S3;
- migrations manuais devem ser idempotentes e compatíveis com dados existentes;
- mensagens e documentação operacionais em português;
- identificadores de código em inglês.

## Pull requests

Descreva:

- problema e motivação;
- solução adotada;
- risco para dados e segurança;
- testes executados;
- forma de deploy e rollback;
- documentação alterada.

## Segurança

Não abra issue pública para vulnerabilidade ou segredo exposto. Siga [SECURITY.md](SECURITY.md).
