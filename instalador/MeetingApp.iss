; MeetingApp — instalador (Fase 4)
;
; Quem monta o payload e chama isto é tools/montar_instalador.sh. Compilar este
; arquivo na mão produz um instalador sem as réguas, que é exatamente o defeito
; que o publicar.sh existe para evitar do outro lado.
;
; As decisões que este arquivo materializa estão em docs/FASE4.md §6. As três
; que mais custam se erradas:
;
;   1. **por usuário, sem UAC.** Não é só conveniência: o GGUF da ata é baixado
;      para motores\ata\modelos, ao lado do llama-server que o abre por caminho.
;      Em Program Files esse download falharia com acesso negado;
;   2. **AppMutex.** O app é bandeja: ele está aberto quase sempre, e pode estar
;      GRAVANDO. Instalar por cima de um .exe em execução falha no meio da cópia;
;   3. **desinstalar não apaga dado.** Gravação, transcrição, ata, notas, vozes e
;      modelos ficam. O desinstalador diz onde estão.
;
; Variáveis que o script passa:
;   /DVersao=0.1.0      a mesma de app-net/Directory.Build.props
;   /DPayload=<pasta>   os arquivos pequenos: .exe, DLL, docs, ícone, bootstrapper
;   /DMotores=<pasta>   a árvore de motores\, lida ONDE ELA JÁ ESTÁ
;   /DSaida=<pasta>     onde o .exe do instalador é escrito
;
; Payload e Motores são separados por um motivo prático: os motores são 5,4 GB, e
; copiá-los para uma pasta de estágio a cada build custaria minutos e 5,4 GB de
; disco para produzir os mesmos bytes. O Inno lê de qualquer caminho, então ele lê
; da instalação que já existe — e o que não deve viajar sai por Excludes, não por
; uma cópia seletiva.

#ifndef Versao
  #error Falta /DVersao — rode por tools/montar_instalador.sh
#endif
#ifndef Payload
  #error Falta /DPayload — rode por tools/montar_instalador.sh
#endif
#ifndef Motores
  #error Falta /DMotores — rode por tools/montar_instalador.sh
#endif
#ifndef Saida
  #define Saida "."
#endif

[Setup]
; O AppId nunca muda. É por ele que o Windows sabe que 0.1.1 é atualização de
; 0.1.0, e não um segundo programa; trocá-lo deixaria duas entradas em
; "Aplicativos Instalados" e duas pastas de 5 GB.
AppId={{8B6F3A21-4C5E-4E17-9A2D-1F0B7C4E9D33}
AppName=MeetingApp
AppVersion={#Versao}
AppVerName=MeetingApp {#Versao}
VersionInfoVersion={#Versao}
AppPublisher=decoworship
DefaultDirName={localappdata}\Programs\MeetingApp
DefaultGroupName=MeetingApp
DisableProgramGroupPage=yes
DisableDirPage=no
; Sem UAC: instalação por usuário. Ver o motivo 1 no cabeçalho.
PrivilegesRequired=lowest
; Sem PrivilegesRequiredOverridesAllowed de propósito. Deixar escolher "para
; todos os usuários" instalaria em {localappdata} do administrador, e o app
; abriria para a pessoa errada — ou não abriria. A instalação é por usuário,
; ponto, e é o que o DefaultDirName pressupõe.
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; O mutex que o próprio app cria (App/Program.cs). Com ele, instalar com o app
; aberto vira um pedido educado para fechar, em vez de um erro de cópia no meio.
AppMutex=Global\MeetingApp
OutputDir={#Saida}
OutputBaseFilename=MeetingApp-{#Versao}-instalador
SetupIconFile={#Payload}\logo.ico
UninstallDisplayIcon={app}\MeetingApp.exe
; lzma2/max e solid: o payload é dominado por DLLs de CUDA, que comprimem bem, e
; o instalador é entregue por link — cada 100 MB conta mais que o minuto a mais
; de compressão.
Compression=lzma2/max
SolidCompression=yes
LZMANumBlockThreads=4
WizardStyle=modern
; Sem assinatura de código nesta versão (docs/FASE4.md §6): o SmartScreen vai
; avisar, e o INSTALAR.md diz o que fazer.

[Languages]
Name: "brazilian"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Tasks]
Name: "atalhonaarea"; Description: "Criar atalho na área de trabalho"; \
  GroupDescription: "Atalhos:"
; Marcada por padrão, e de propósito: um gravador de reunião que não está aberto
; quando a reunião começa não grava nada.
Name: "iniciarcomwindows"; Description: "Iniciar o MeetingApp junto com o Windows"; \
  GroupDescription: "Ao ligar o computador:"

[Files]
Source: "{#Payload}\MeetingApp.exe"; DestDir: "{app}"; Flags: ignoreversion
; O carregador nativo do WebView2 não entra no single-file: ele é carregado por
; nome, do disco, antes de o host gerenciado existir.
Source: "{#Payload}\WebView2Loader.dll"; DestDir: "{app}"; Flags: ignoreversion
; Os motores — Python embarcado, os três sidecars, o llama.cpp e os 57 MB de
; pesos de diarização. São dezenas de milhares de arquivos.
;
; O que NÃO viaja, e cada exclusão é uma decisão:
;   *.gguf      os modelos de ata, 2,5 GB cada. Decisão (2) da fase: modelos
;               baixam na primeira execução, pela tela que já sabe baixá-los;
;   .cache      o que o huggingface_hub deixa para trás ao baixar;
;   __pycache__ bytecode desta máquina, que o Python regenera sozinho.
Source: "{#Motores}\*"; DestDir: "{app}\motores"; \
  Excludes: "*.gguf,.cache,__pycache__"; \
  Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#Payload}\INSTALAR.md"; DestDir: "{app}"; Flags: ignoreversion isreadme
Source: "{#Payload}\CHANGELOG.md"; DestDir: "{app}"; Flags: ignoreversion
; O bootstrapper do WebView2, executado só se a runtime não estiver presente.
Source: "{#Payload}\MicrosoftEdgeWebview2Setup.exe"; DestDir: "{tmp}"; \
  Flags: deleteafterinstall; Check: not TemWebView2

[Icons]
Name: "{group}\MeetingApp"; Filename: "{app}\MeetingApp.exe"
Name: "{userdesktop}\MeetingApp"; Filename: "{app}\MeetingApp.exe"; \
  Tasks: atalhonaarea

[Registry]
; Inicialização por usuário, na chave Run do próprio usuário — não há serviço,
; não há tarefa agendada, e desinstalar leva a chave junto.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; \
  ValueType: string; ValueName: "MeetingApp"; ValueData: """{app}\MeetingApp.exe"""; \
  Flags: uninsdeletevalue; Tasks: iniciarcomwindows

[Run]
Filename: "{tmp}\MicrosoftEdgeWebview2Setup.exe"; Parameters: "/silent /install"; \
  StatusMsg: "Instalando o componente WebView2 do Windows…"; \
  Check: not TemWebView2; Flags: waituntilterminated
Filename: "{app}\MeetingApp.exe"; Description: "Abrir o MeetingApp"; \
  Flags: nowait postinstall skipifsilent

[UninstallDelete]
; O que o app escreve dentro da própria pasta e o instalador não instalou: os
; modelos baixados pela tela de Modelos. Sem estas linhas, a pasta fica para trás
; com gigabytes dentro e o Windows a lista como "não removida completamente".
;
; Note que isto apaga MODELO, que se rebaixa. Gravação, transcrição, ata, notas e
; vozes moram fora da pasta do app e não são tocadas — ver a mensagem do
; CurUninstallStepChanged.
Type: filesandordirs; Name: "{app}\motores\ata\modelos"
Type: dirifempty; Name: "{app}\motores"
Type: dirifempty; Name: "{app}"

[Code]

{ A runtime do WebView2 está nesta máquina?

  Windows 11 já vem com ela, e o Windows 10 quase sempre a tem pelo Edge. Mas
  "quase sempre" na máquina de outra pessoa é uma janela em branco sem
  explicação, então o bootstrapper vai junto (1,7 MB) e roda só quando falta.

  Três lugares para procurar, e é preciso os três: a instalação por máquina
  aparece sob WOW6432Node em Windows de 64 bits, e a por usuário só no HKCU. }
{ Sem seção `const` dentro da função: o Pascal Script do Inno não a aceita, e o
  erro que ele dá — "'BEGIN' expected" na linha do const — não diz isso. }
function TemWebView2: Boolean;
var
  Versao, Cliente: String;
begin
  Cliente := '{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}';
  Result :=
    (RegQueryStringValue(HKLM, 'SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\' + Cliente, 'pv', Versao) and (Versao <> '') and (Versao <> '0.0.0.0')) or
    (RegQueryStringValue(HKLM, 'SOFTWARE\Microsoft\EdgeUpdate\Clients\' + Cliente, 'pv', Versao) and (Versao <> '') and (Versao <> '0.0.0.0')) or
    (RegQueryStringValue(HKCU, 'SOFTWARE\Microsoft\EdgeUpdate\Clients\' + Cliente, 'pv', Versao) and (Versao <> '') and (Versao <> '0.0.0.0'));
end;

{ A instalação manual que já existe, de antes de haver instalador.

  Nesta máquina o app mora em C:\Users\<voce>\MeetingApp, montado à mão pelo
  publicar.sh, com o GGUF de 2,5 GB dentro. O instalador aponta para outro lugar,
  e sem isto ficariam duas instalações e dois motores\.

  O que ele faz é conservador de propósito, porque roda uma vez na vida de uma
  máquina: **move o que reconhece e não apaga o que não reconhece.** Os modelos de
  transcrição não entram aqui porque não precisam — eles moram no cache do
  HuggingFace, em %USERPROFILE%, que nenhuma das duas instalações toca. }
function PastaAntiga: String;
begin
  Result := ExpandConstant('{%USERPROFILE}\MeetingApp');
  if not DirExists(Result) then
    Result := '';
end;

var
  MoverModelos: Boolean;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Antiga, Origem, Destino, Arquivo: String;
  Busca: TFindRec;
begin
  if CurStep <> ssPostInstall then Exit;
  if not MoverModelos then Exit;

  Antiga := PastaAntiga;
  if Antiga = '' then Exit;

  Origem := Antiga + '\motores\ata\modelos';
  Destino := ExpandConstant('{app}\motores\ata\modelos');
  if not DirExists(Origem) then Exit;

  ForceDirectories(Destino);
  if FindFirst(Origem + '\*.gguf', Busca) then
  begin
    try
      repeat
        Arquivo := Busca.Name;
        { RenameFile entre pastas do mesmo volume é instantâneo e não duplica
          2,5 GB em disco. Se falhar (volumes diferentes), deixa como está: o
          usuário baixa de novo, que é chato, e não perde nada. }
        if not FileExists(Destino + '\' + Arquivo) then
          RenameFile(Origem + '\' + Arquivo, Destino + '\' + Arquivo);
      until not FindNext(Busca);
    finally
      FindClose(Busca);
    end;
  end;
end;

function InitializeSetup: Boolean;
begin
  Result := True;
  MoverModelos := False;

  if PastaAntiga <> '' then
    MoverModelos := MsgBox(
      'Encontrei uma instalação anterior em:' + #13#10#13#10 +
      PastaAntiga + #13#10#13#10 +
      'Ela é de antes de existir instalador. Quer que eu aproveite os modelos ' +
      { Um #13#10 no começo da linha vira diretiva de pré-processador para o
        ISPP, e o compilador para com "Unknown preprocessor directive". Por isso
        ele fica grudado no fim da linha anterior, e não no começo desta. }
      'já baixados dela? São alguns GB que não precisam ser baixados de novo.' + #13#10#13#10 +
      'A pasta antiga NÃO será apagada — depois de conferir que o app novo ' +
      'funciona, você pode removê-la à mão.',
      mbConfirmation, MB_YESNO) = IDYES;
end;

{ O que fica para trás, dito com o caminho.

  Um desinstalador que apaga reunião é um desastre irreversível; um que deixa
  gigabytes sem avisar é um mistério. O caminho do meio é não apagar e dizer
  onde está. }
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep <> usPostUninstall then Exit;

  MsgBox(
    'O MeetingApp foi removido.' + #13#10#13#10 +
    'O que NÃO foi apagado, de propósito:' + #13#10#13#10 +
    '• as gravações, transcrições, atas e notas' + #13#10 +
    '• as configurações e as vozes aprendidas, em:' + #13#10 +
    '   ' + ExpandConstant('{%USERPROFILE}') + '\.meeting-transcription' + #13#10 +
    '• os modelos de transcrição baixados, em:' + #13#10 +
    '   ' + ExpandConstant('{%USERPROFILE}') + '\.cache\huggingface' + #13#10#13#10 +
    'Se quiser apagar tudo, remova essas pastas à mão.',
    mbInformation, MB_OK);
end;
