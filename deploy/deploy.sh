#!/bin/bash
set -e

# TRAT ControlPlane Production Deployment Script
# Ubuntu 24.04 LTS with Docker + Portainer

echo "=================================================="
echo "TRAT ControlPlane - Production Deployment"
echo "=================================================="

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

# Function to print colored messages
print_success() {
    echo -e "${GREEN}✓ $1${NC}"
}

print_error() {
    echo -e "${RED}✗ $1${NC}"
    exit 1
}

print_info() {
    echo -e "${YELLOW}→ $1${NC}"
}

# Check if running as root
if [[ $EUID -ne 0 ]]; then
   print_error "Este script precisa ser executado como root (use sudo)"
fi

# Step 1: Check prerequisites
print_info "Verificando pré-requisitos..."

if ! command -v docker &> /dev/null; then
    print_error "Docker não está instalado. Execute: apt update && apt install docker.io docker-compose-plugin"
fi
print_success "Docker instalado"

if ! command -v docker compose &> /dev/null; then
    print_error "Docker Compose não está instalado. Execute: apt install docker-compose-plugin"
fi
print_success "Docker Compose instalado"

# Step 2: Create deployment directory
DEPLOY_DIR="/opt/trat-controlplane"
print_info "Criando diretório de deployment em $DEPLOY_DIR..."
mkdir -p "$DEPLOY_DIR"
cd "$DEPLOY_DIR"
print_success "Diretório criado"

# Step 3: Clone or update repository
print_info "Baixando código do repositório..."
if [ -d ".git" ]; then
    git pull origin main || print_error "Falha ao fazer pull do repositório"
else
    git clone https://seu-repo-aqui.git . || print_error "Falha ao clonar repositório"
fi
print_success "Código atualizado"

# Step 4: Copy docker files
print_info "Copiando configurações Docker..."
cp deploy/docker-compose.yml .
cp deploy/nginx.conf .
cp deploy/Dockerfile .
cp deploy/appsettings.Production.json .
print_success "Arquivos Docker copiados"

# Step 5: Create .env file
print_info "Configurando variáveis de ambiente..."
if [ ! -f ".env" ]; then
    cp deploy/.env.template .env
    print_info "Arquivo .env criado a partir do template"
    print_error "⚠️  IMPORTANTE: Edite o arquivo .env e configure as senhas seguras!"
    echo "   Execute: nano /opt/trat-controlplane/.env"
    echo "   Depois: sudo bash $0"
    exit 1
fi
print_success "Arquivo .env já existe"

# Step 6: Create SSL directory
print_info "Preparando diretório SSL..."
mkdir -p ssl
print_success "Diretório SSL criado"

# Step 7: Check if certificate exists
if [ ! -f "ssl/fullchain.pem" ] || [ ! -f "ssl/privkey.pem" ]; then
    print_info "Certificado SSL não encontrado"
    print_error "Execute o script de certificado Let's Encrypt primeiro:"
    echo ""
    echo "   # Instale certbot:"
    echo "   sudo apt install certbot python3-certbot-nginx"
    echo ""
    echo "   # Gere o certificado:"
    echo "   sudo certbot certonly --standalone -d trat.outboxtech.com.br"
    echo ""
    echo "   # Copie os certificados:"
    echo "   sudo cp /etc/letsencrypt/live/trat.outboxtech.com.br/fullchain.pem $DEPLOY_DIR/ssl/"
    echo "   sudo cp /etc/letsencrypt/live/trat.outboxtech.com.br/privkey.pem $DEPLOY_DIR/ssl/"
    echo "   sudo chown $USER:$USER $DEPLOY_DIR/ssl/*"
    echo ""
    exit 1
fi
print_success "Certificados SSL encontrados"

# Step 8: Build ControlPlane image
print_info "Construindo imagem Docker do ControlPlane..."
docker build -f Dockerfile -t trat-controlplane:latest . || print_error "Falha ao construir imagem Docker"
print_success "Imagem Docker construída"

# Step 9: Start services
print_info "Iniciando containers (PostgreSQL, ControlPlane, Nginx)..."
docker compose up -d || print_error "Falha ao iniciar containers"
print_success "Containers iniciados"

# Step 10: Wait for services to be ready
print_info "Aguardando serviços ficarem prontos..."
sleep 15

# Step 11: Check health
print_info "Verificando saúde dos serviços..."
if docker compose ps | grep -q "healthy"; then
    print_success "Serviços estão saudáveis"
else
    print_info "Verificando logs..."
    docker compose logs --tail=20
fi

# Step 12: Verify database
print_info "Verificando banco de dados PostgreSQL..."
if docker compose exec -T postgres pg_isready -U trat_admin -d trat_controlplane &> /dev/null; then
    print_success "PostgreSQL está pronto"
else
    print_error "PostgreSQL não está respondendo"
fi

# Step 13: Test API
print_info "Testando API..."
if curl -s -f http://localhost:5000/api/v1/health > /dev/null; then
    print_success "API está respondendo"
else
    print_error "API não está respondendo. Verificar logs: docker compose logs controlplane"
fi

# Step 14: Display summary
echo ""
echo "=================================================="
print_success "Deployment concluído com sucesso!"
echo "=================================================="
echo ""
echo "📋 Informações de Acesso:"
echo "   URL: https://trat.outboxtech.com.br"
echo "   Email: $(grep ADMIN_EMAIL .env | cut -d= -f2)"
echo ""
echo "📝 Próximos Passos:"
echo "   1. Acesse https://trat.outboxtech.com.br"
echo "   2. Login com o email e senha do admin"
echo "   3. Crie seu primeiro cliente"
echo "   4. Configure o agente nos servidores"
echo ""
echo "🔧 Comandos Úteis:"
echo "   # Ver logs:"
echo "   docker compose logs -f"
echo ""
echo "   # Ver status:"
echo "   docker compose ps"
echo ""
echo "   # Parar serviços:"
echo "   docker compose down"
echo ""
echo "   # Backup do banco:"
echo "   docker compose exec postgres pg_dump -U trat_admin trat_controlplane > backup-\$(date +%s).sql"
echo ""
echo "=================================================="
