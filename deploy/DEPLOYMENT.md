# TRAT ControlPlane - Guia de Deployment Produção

Deployment de TRAT ControlPlane em Docker + PostgreSQL na Contabo com domínio `trat.outboxtech.com.br`.

## 📋 Pré-requisitos

- **VPS Contabo** com Ubuntu 24.04.4 LTS
- **Domínio** outboxtech.com.br com acesso a DNS
- **SSH** access à VPS com permissões de sudo
- **Git** instalado na VPS

## 🚀 Passo a Passo Completo

### Fase 1: Preparação da VPS (SSH)

#### 1.1 Conecte à VPS
```bash
ssh root@seu-ip-vps
# ou
ssh user@seu-ip-vps
sudo -i  # se precisar de root
```

#### 1.2 Atualize o sistema
```bash
apt update && apt upgrade -y
```

#### 1.3 Instale Docker e Docker Compose
```bash
apt install -y docker.io docker-compose-plugin git curl wget
usermod -aG docker $USER  # se não for root
```

Logout e login novamente para aplicar o grupo docker.

#### 1.4 Verifique as instalações
```bash
docker --version
docker compose version
git --version
```

### Fase 2: Preparar Certificado SSL (Let's Encrypt)

#### 2.1 Instale Certbot
```bash
apt install -y certbot python3-certbot-nginx
```

#### 2.2 Configure DNS
Antes de gerar o certificado, certifique-se que `trat.outboxtech.com.br` aponta para o IP da sua VPS:

1. Acesse o painel de DNS do seu provedor
2. Crie um registro A: `trat.outboxtech.com.br` → `SEU_IP_VPS`
3. Aguarde propagação (5-30 minutos)

Teste com:
```bash
nslookup trat.outboxtech.com.br
# Deve retornar seu IP
```

#### 2.3 Gere o certificado
```bash
certbot certonly --standalone -d trat.outboxtech.com.br

# Escolha as opções (leave email in blank é ok, agree to terms)
# Certificado será gerado em: /etc/letsencrypt/live/trat.outboxtech.com.br/
```

### Fase 3: Clonar e Configurar TRAT

#### 3.1 Crie diretório de deployment
```bash
mkdir -p /opt/trat-controlplane
cd /opt/trat-controlplane
```

#### 3.2 Clone o repositório
```bash
git clone https://github.com/seu-usuario/trat.git .
# Ou se for privado, use seu token/SSH key

cd /opt/trat-controlplane
```

#### 3.3 Copie arquivos Docker
```bash
cp deploy/docker-compose.yml .
cp deploy/nginx.conf .
cp deploy/Dockerfile .
cp deploy/appsettings.Production.json .
cp deploy/.env.template .env
mkdir -p ssl
```

#### 3.4 Configure arquivo .env
```bash
nano .env
```

**Edite os seguintes valores:**

```bash
# PostgreSQL - Gere uma senha segura (32+ caracteres)
DB_PASSWORD=gerar-password-segura-aqui

# Security tokens - Use openssl rand -base64 32
AGENT_TOKEN=gerar-token-seguro-aqui
ADMIN_TOKEN=gerar-token-seguro-aqui

# Admin inicial
ADMIN_EMAIL=seu-email@outboxtech.com.br
ADMIN_PASSWORD=senha-forte-aqui-12-chars-minimo
```

**Para gerar senhas seguras:**
```bash
openssl rand -base64 32
```

#### 3.5 Copie certificados SSL
```bash
sudo cp /etc/letsencrypt/live/trat.outboxtech.com.br/fullchain.pem /opt/trat-controlplane/ssl/
sudo cp /etc/letsencrypt/live/trat.outboxtech.com.br/privkey.pem /opt/trat-controlplane/ssl/
sudo chown $(whoami):$(whoami) /opt/trat-controlplane/ssl/*
chmod 644 /opt/trat-controlplane/ssl/*
```

### Fase 4: Build e Deploy

#### 4.1 Construa a imagem Docker
```bash
cd /opt/trat-controlplane
docker build -f Dockerfile -t trat-controlplane:latest .
```

Isso pode levar 3-5 minutos na primeira vez.

#### 4.2 Inicie os containers
```bash
docker compose up -d
```

#### 4.3 Verifique se está rodando
```bash
docker compose ps

# Deve mostrar 3 containers:
# - trat-postgres (PostgreSQL)
# - trat-controlplane (API)
# - trat-nginx (Nginx)
```

#### 4.4 Aguarde o startup
```bash
sleep 20
docker compose logs controlplane | tail -20
```

Procure por mensagens como:
```
[info] Aplicação inicializada
[info] Banco de dados pronto
```

### Fase 5: Validação

#### 5.1 Teste a API
```bash
curl -s http://localhost:5000/api/v1/health
# Deve retornar um status 200 OK
```

#### 5.2 Acesse via HTTPS
Abra navegador: https://trat.outboxtech.com.br

**Você deve ver:**
- ✅ Certificado válido (sem avisos SSL)
- ✅ Login page do TRAT
- ✅ Email/senha do admin

#### 5.3 Login inicial
1. Email: valor de `ADMIN_EMAIL` no .env
2. Senha: valor de `ADMIN_PASSWORD` no .env

### Fase 6: Setup Inicial (Portainer - Opcional)

Se quiser gerenciar via Portainer:

#### 6.1 Instale Portainer
```bash
docker volume create portainer_data

docker run -d \
  --name portainer \
  --restart always \
  -p 8000:8000 \
  -p 9443:9443 \
  -v /var/run/docker.sock:/var/run/docker.sock \
  -v portainer_data:/data \
  portainer/portainer-ce:2.20.3
```

#### 6.2 Acesse Portainer
```
https://seu-ip-vps:9443
```

Configure password inicial, depois acesse Environment Local e veja sua stack TRAT.

## 📊 Gestão Operacional

### Logs
```bash
# Ver logs em tempo real
docker compose logs -f

# Ver apenas ControlPlane
docker compose logs -f controlplane

# Ver apenas PostgreSQL
docker compose logs -f postgres

# Ver últimas 50 linhas
docker compose logs --tail=50
```

### Backup do Banco
```bash
docker compose exec postgres pg_dump -U trat_admin trat_controlplane > backup-$(date +%s).sql
```

Restaurar backup:
```bash
docker compose exec -T postgres psql -U trat_admin trat_controlplane < backup-1234567890.sql
```

### Parar/Reiniciar
```bash
# Parar tudo
docker compose down

# Reiniciar tudo
docker compose up -d

# Reiniciar apenas uma aplicação
docker compose restart controlplane
```

### Atualizar código
```bash
cd /opt/trat-controlplane
git pull origin main
docker build -f Dockerfile -t trat-controlplane:latest .
docker compose up -d  # Reinicia com nova imagem
```

### Monitorar uso de recursos
```bash
docker stats
```

## 🔐 Segurança

### 1. Renovação de Certificado SSL (automático via cron)
```bash
# Adicione ao crontab:
crontab -e

# Adicione a linha:
0 3 * * * certbot renew --quiet
```

### 2. Firewall UFW (recomendado)
```bash
ufw enable
ufw allow 22/tcp    # SSH
ufw allow 80/tcp    # HTTP
ufw allow 443/tcp   # HTTPS
ufw allow 8000/tcp  # Portainer (opcional)
ufw allow 9443/tcp  # Portainer (opcional)
```

### 3. Backup automático do banco
```bash
# Criar script de backup
cat > /opt/trat-controlplane/backup.sh << 'EOF'
#!/bin/bash
BACKUP_DIR="/opt/trat-controlplane/backups"
mkdir -p $BACKUP_DIR
TIMESTAMP=$(date +%Y%m%d_%H%M%S)
docker compose -f /opt/trat-controlplane/docker-compose.yml exec -T postgres \
  pg_dump -U trat_admin trat_controlplane > $BACKUP_DIR/backup_$TIMESTAMP.sql
# Manter apenas últimos 30 dias
find $BACKUP_DIR -name "backup_*.sql" -mtime +30 -delete
EOF

chmod +x /opt/trat-controlplane/backup.sh

# Agende via crontab (daily at 2am)
crontab -e
# Adicione: 0 2 * * * /opt/trat-controlplane/backup.sh
```

## 🐛 Troubleshooting

### Containers não iniciam
```bash
docker compose logs --tail=100
# Procure por erros de conexão PostgreSQL ou porta em uso
```

### Erro de conexão PostgreSQL
```bash
# Verifique se PostgreSQL está saudável
docker compose logs postgres

# Reinicie PostgreSQL
docker compose restart postgres
```

### Certificado SSL inválido
```bash
# Renove manualmente
certbot renew --force-renewal -d trat.outboxtech.com.br

# Copie novamente os certificados
sudo cp /etc/letsencrypt/live/trat.outboxtech.com.br/fullchain.pem /opt/trat-controlplane/ssl/
sudo cp /etc/letsencrypt/live/trat.outboxtech.com.br/privkey.pem /opt/trat-controlplane/ssl/

# Reinicie nginx
docker compose restart nginx
```

### Porta 80 ou 443 já em uso
```bash
# Encontre o processo usando a porta
lsof -i :80
lsof -i :443

# Mate o processo ou mude para porta diferente em docker-compose.yml
```

## ✅ Checklist Final

- [ ] VPS com Ubuntu 24.04 LTS online
- [ ] Docker e Docker Compose instalados
- [ ] DNS `trat.outboxtech.com.br` apontando para VPS
- [ ] Certificado SSL Let's Encrypt gerado
- [ ] Arquivo `.env` configurado com senhas seguras
- [ ] Containers iniciados sem erros
- [ ] API respondendo em http://localhost:5000/api/v1/health
- [ ] HTTPS funcionando em https://trat.outboxtech.com.br
- [ ] Login com admin inicial funcionando
- [ ] Backup do banco configurado via crontab
- [ ] Firewall UFW configurado (opcional)

## 📞 Suporte

Para issues, verifique:
1. Logs: `docker compose logs -f`
2. Certificado: `certbot certificates`
3. DNS: `nslookup trat.outboxtech.com.br`
4. Conectividade: `telnet localhost 5000`

---

**Deployment pronto! Seu TRAT ControlPlane está live em produção. 🚀**
