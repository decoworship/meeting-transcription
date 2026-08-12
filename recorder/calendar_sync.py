"""Associa uma gravação ao evento do Google Calendar que está acontecendo.

O objetivo não é só rotular a gravação: os participantes do evento alimentam o
vocabulário customizado do transcritor, que é o que faz nomes próprios pararem
de virar outra coisa.

Regra de ouro: **nada aqui pode atrasar ou impedir uma gravação**. Rede caindo,
token expirado ou nenhum evento encontrado devolvem ``None`` e a gravação segue.
Por isso a consulta roda em thread separada, depois que a captura já começou.

Credenciais ficam em ``%USERPROFILE%\\.meeting-recorder``, junto do ambiente
descartável -- nunca no repositório.
"""

from __future__ import annotations

import datetime as dt
import logging
from dataclasses import dataclass, field
from pathlib import Path
from typing import Optional

logger = logging.getLogger(__name__)

BASE = Path.home() / ".meeting-recorder"
CLIENT_SECRET = BASE / "google_client_secret.json"
TOKEN_PATH = BASE / "google_token.json"
# Qual conta o token representa. Guardado à parte porque a bandeja precisa
# mostrar isso a cada abertura de menu, e consultar a API para isso seria
# uma chamada de rede por clique.
ACCOUNT_PATH = BASE / "google_account.json"

# Somente leitura: o gravador não tem motivo para escrever na agenda.
SCOPES = ["https://www.googleapis.com/auth/calendar.readonly"]

# Uma reunião raramente começa no minuto exato. Se nenhum evento cobre o
# instante atual, aceita o mais próximo dentro desta janela.
NEAREST_WINDOW_MIN = 15


# Por que status e não só ``None``: um calendário que nunca foi configurado e um
# token que morreu produzem o mesmo resultado (sem evento), mas exigem reações
# opostas -- silêncio no primeiro caso, aviso no segundo. Sem essa distinção o
# gravador pararia de identificar reuniões sem ninguém perceber, que é
# exatamente o tipo de falha silenciosa que já nos custou uma gravação.
OK = "ok"
NO_EVENT = "no_event"
NOT_CONFIGURED = "not_configured"
NOT_AUTHORIZED = "not_authorized"
TOKEN_EXPIRED = "token_expired"
ERROR = "error"

# Status que merecem interromper o usuário: houve autorização e ela quebrou.
ACTIONABLE = {TOKEN_EXPIRED, ERROR}


@dataclass
class Lookup:
    """Resultado de uma consulta: o evento (talvez) e por que não veio."""
    event: Optional["Event"] = None
    status: str = NO_EVENT
    detail: str = ""

    @property
    def needs_attention(self) -> bool:
        return self.status in ACTIONABLE


@dataclass
class Event:
    """O que interessa de um evento, já achatado."""
    event_id: str
    title: str
    start: Optional[str] = None
    end: Optional[str] = None
    attendees: list[dict] = field(default_factory=list)
    organizer: Optional[str] = None

    @property
    def attendee_names(self) -> list[str]:
        """Nomes para alimentar o vocabulário. Sem e-mails, sem salas."""
        nomes = []
        for a in self.attendees:
            if a.get("resource"):          # salas e equipamentos não falam
                continue
            nome = (a.get("displayName") or "").strip()
            if not nome:
                # Sem displayName, o local part do e-mail costuma ser o nome:
                # "dimi.randel@..." -> "Dimi Randel"
                email = (a.get("email") or "").split("@")[0]
                nome = " ".join(p.capitalize() for p in email.replace(".", " ").split())
            if nome and nome not in nomes:
                nomes.append(nome)
        return nomes

    def to_meta(self) -> dict:
        """No formato que o meta.json do gravador já reserva."""
        return {
            "title": self.title,
            "client": None,          # preenchido depois pelo transcritor
            "project": None,
            "attendees": self.attendee_names,
            "calendar_event_id": self.event_id,
            "start": self.start,
            "end": self.end,
            "organizer": self.organizer,
        }


_expired_detail: str = ""


def _mark_expired(detail: str) -> None:
    global _expired_detail
    _expired_detail = detail


def is_configured() -> bool:
    return CLIENT_SECRET.is_file()


def is_authorized() -> bool:
    return TOKEN_PATH.is_file()


def _load_credentials(interactive: bool):
    """Devolve credenciais válidas, ou None.

    Com ``interactive=False`` nunca abre navegador: serve para o caminho
    automático, que roda durante a gravação e não pode roubar o foco.
    """
    try:
        from google.oauth2.credentials import Credentials
        from google.auth.transport.requests import Request
        from google_auth_oauthlib.flow import InstalledAppFlow
    except ImportError as e:
        logger.warning(f"bibliotecas do Google ausentes: {e}")
        return None

    creds = None
    if TOKEN_PATH.is_file():
        try:
            creds = Credentials.from_authorized_user_file(str(TOKEN_PATH), SCOPES)
        except Exception as e:
            logger.warning(f"token ilegível, será refeito: {e}")
            creds = None

    if creds and creds.valid:
        return creds

    if creds and creds.expired and creds.refresh_token:
        try:
            creds.refresh(Request())
            TOKEN_PATH.write_text(creds.to_json(), encoding="utf-8")
            return creds
        except Exception as e:
            # Caso clássico: app em "Testing" no Google Cloud, onde o Google
            # expira todo refresh token em 7 dias. Publicar em Production
            # resolve. Marcamos para o chamador poder avisar em vez de degradar
            # em silêncio.
            logger.warning(f"refresh do token falhou: {e}")
            _mark_expired(str(e))

    if not interactive:
        return None

    if not CLIENT_SECRET.is_file():
        logger.error(f"credencial OAuth não encontrada em {CLIENT_SECRET}")
        return None

    flow = InstalledAppFlow.from_client_secrets_file(str(CLIENT_SECRET), SCOPES)
    # "select_account" força o seletor de contas. Só "consent" reaproveita a
    # sessão do navegador e conecta de novo a mesma conta -- inútil para quem
    # quer trocar da conta pessoal para a da empresa.
    creds = flow.run_local_server(port=0, prompt="select_account consent")
    BASE.mkdir(parents=True, exist_ok=True)
    TOKEN_PATH.write_text(creds.to_json(), encoding="utf-8")
    logger.info(f"autorizado; token salvo em {TOKEN_PATH}")
    _store_account(creds)
    return creds


def _store_account(creds) -> None:
    """Descobre e guarda o e-mail da conta conectada.

    O id do calendário "primary" é o próprio endereço, então dá para saber a
    conta sem pedir nenhum escopo de identidade além do que já temos.
    """
    try:
        from googleapiclient.discovery import build
        service = build("calendar", "v3", credentials=creds, cache_discovery=False)
        cal = service.calendars().get(calendarId="primary").execute()
        email = cal.get("id", "")
        if email:
            ACCOUNT_PATH.write_text(
                __import__("json").dumps({"email": email}), encoding="utf-8")
            logger.info(f"conta conectada: {email}")
    except Exception as e:
        # Saber a conta é conveniência; não vale derrubar a autorização por isso.
        logger.debug(f"não foi possível identificar a conta: {e}")


def account_email() -> str:
    """E-mail da conta conectada, ou string vazia. Nunca faz rede."""
    if not ACCOUNT_PATH.is_file():
        return ""
    try:
        import json as _json
        return _json.loads(ACCOUNT_PATH.read_text(encoding="utf-8")).get("email", "")
    except (OSError, ValueError):
        return ""


def disconnect() -> None:
    """Esquece a conta atual. A próxima autorização começa do zero."""
    for p in (TOKEN_PATH, ACCOUNT_PATH):
        try:
            p.unlink(missing_ok=True)
        except OSError as e:
            logger.warning(f"não foi possível remover {p}: {e}")
    logger.info("conta do Google desconectada")


def authorize() -> bool:
    """Fluxo interativo, com seletor de contas.

    Remove o token antes de começar: com um token válido em disco o fluxo
    devolveria as credenciais existentes sem abrir o navegador, e trocar de
    conta seria impossível.
    """
    disconnect()
    return _load_credentials(interactive=True) is not None


def current_event(when: Optional[dt.datetime] = None,
                  window_min: int = NEAREST_WINDOW_MIN) -> Lookup:
    """Evento acontecendo agora, ou o mais próximo dentro da janela.

    Nunca levanta: qualquer falha vira um ``Lookup`` sem evento e a gravação
    segue sem rótulo. O ``status`` diz se aquilo exige ação do usuário.
    """
    global _expired_detail
    _expired_detail = ""
    try:
        if not CLIENT_SECRET.is_file():
            return Lookup(status=NOT_CONFIGURED)

        creds = _load_credentials(interactive=False)
        if creds is None:
            if _expired_detail:
                logger.warning("token do calendário expirou; reautorize")
                return Lookup(status=TOKEN_EXPIRED, detail=_expired_detail)
            if not TOKEN_PATH.is_file():
                return Lookup(status=NOT_AUTHORIZED)
            return Lookup(status=TOKEN_EXPIRED, detail="credenciais inválidas")

        from googleapiclient.discovery import build

        agora = when or dt.datetime.now(dt.timezone.utc)
        if agora.tzinfo is None:
            agora = agora.astimezone()
        margem = dt.timedelta(minutes=window_min)

        service = build("calendar", "v3", credentials=creds, cache_discovery=False)
        resp = service.events().list(
            calendarId="primary",
            timeMin=(agora - margem).isoformat(),
            timeMax=(agora + margem).isoformat(),
            singleEvents=True,
            orderBy="startTime",
            maxResults=20,
        ).execute()

        itens = [e for e in resp.get("items", [])
                 if e.get("status") != "cancelled" and e.get("summary")]
        if not itens:
            logger.info("nenhum evento na janela")
            return Lookup(status=NO_EVENT)

        escolhido = _pick(itens, agora)
        if escolhido is None:
            return Lookup(status=NO_EVENT)

        ev = Event(
            event_id=escolhido.get("id", ""),
            title=escolhido.get("summary", "").strip(),
            start=(escolhido.get("start") or {}).get("dateTime"),
            end=(escolhido.get("end") or {}).get("dateTime"),
            attendees=escolhido.get("attendees") or [],
            organizer=(escolhido.get("organizer") or {}).get("displayName"),
        )
        logger.info(f"evento: {ev.title!r} com {len(ev.attendee_names)} participantes")
        return Lookup(event=ev, status=OK)

    except Exception as e:
        # Deliberadamente amplo: nenhuma falha de calendário pode contaminar
        # uma gravação em andamento.
        logger.warning(f"consulta ao calendário falhou: {e}")
        return Lookup(status=ERROR, detail=str(e))


def _pick(itens: list[dict], agora: dt.datetime) -> Optional[dict]:
    """Prefere o evento que cobre o instante; senão, o de início mais próximo.

    Empates vão para o mais curto: numa agenda com um bloco de "foco" de 4h e
    uma reunião de 30 min sobrepostos, a reunião é a resposta certa.
    """
    def parse(v):
        if not v:
            return None
        try:
            return dt.datetime.fromisoformat(v.replace("Z", "+00:00"))
        except ValueError:
            return None

    cobrindo, proximos = [], []
    for e in itens:
        ini = parse((e.get("start") or {}).get("dateTime"))
        fim = parse((e.get("end") or {}).get("dateTime"))
        if ini is None:
            continue          # evento de dia inteiro não identifica reunião
        if fim is not None and ini <= agora <= fim:
            cobrindo.append(((fim - ini), e))
        else:
            proximos.append((abs((ini - agora).total_seconds()), e))

    if cobrindo:
        return min(cobrindo, key=lambda x: x[0])[1]
    if proximos:
        return min(proximos, key=lambda x: x[0])[1]
    return None
