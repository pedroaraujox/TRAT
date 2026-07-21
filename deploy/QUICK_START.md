# TRAT ControlPlane - Quick Start (Resumido)

## ⚡ 5 Passos Rápidos

### 1️⃣ SSH na VPS
```bash
ssh root@seu-ip-vps
apt update && apt install -y docker.io docker-compose-plugin git certbot
```

### 2️⃣ Certificado SSL
```bash
# Certifique-se que DNS aponta para VPS
nslookup trat.outboxtech.com.br

# Gere certificado
certbot certonly --standalone -d trat.outboxtech.com.br
```

### 3️⃣ Deploy
```bash
cd /opt
git clone https://seu-repo.git trat-controlplane
cd trat-controlplane

# Copie e edite .env
cp deploy/.env.template .env
nano .env  # MUDAR: DB_PASSWORD, AGENT_TOKEN, ADMIN_TOKEN, ADMIN_PASSWORD

# Copie certificados
mkdir -p ssl
cp /etc/letsencrypt/live/trat.outboxtech.com.br/fullchain.pem ssl/
cp /etc/letsencrypt/live/trat.outboxtech.com.br/privkey.pem ssl/

# Copie arquivos Docker
cp deploy/{docker-compose.yml,nginx.conf,Dockerfile,appsettings.Production.json} .
```

### 4️⃣ Build & Deploy
```bash
docker build -f Dockerfile -t trat-controlplane:latest .
docker compose up -d
sleep 20
docker compose ps
```

### 5️⃣ Acesse
```
https://trat.outboxtech.com.br
Email: seu-email@outboxtech.com.br
Senha: a que configurou em ADMIN_PASSWORD
```

---

## 🎯 Comandos Essenciais

```bash
# Ver status
docker compose ps

# Logs
docker compose logs -f

# Parar
docker compose down

# Backup banco
docker compose exec postgres pg_dump -U trat_admin trat_controlplane > backup.sql
```

---

## ⚠️ IMPORTANTE

1. **Gere senhas fortes para .env:**
   ```bash
   openssl rand -base64 32
   ```

2. **Configure DNS antes do certificado:**
   - `trat.outboxtech.com.br` → seu IP VPS

3. **Guarde as senhas do .env:**
   - Não coloque em git
   - Salve em local seguro

4. **Backup automático:**
   ```bash
   # Adicione ao crontab
   crontab -e
   # 0 2 * * * docker compose -f /opt/trat-controlplane/docker-compose.yml exec -T postgres pg_dump -U trat_admin trat_controlplane > /opt/trat-controlplane/backups/backup_$(date +\%Y\%m\%d_\%H\%M\%S).sql
   ```

---

Para instruções completas, veja `DEPLOYMENT.md`
