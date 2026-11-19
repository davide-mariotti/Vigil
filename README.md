<p align="center">
  <img src="icon/vigil.png" width="112" alt="Vigil icon">
</p>

<h1 align="center">Vigil</h1>

<p align="center">
  Watches your PC's temperatures, fans and power draw from the tray. Works out what's inside on its own.<br>
  <a href="#english">English</a> · <a href="#italiano">Italiano</a> · <a href="https://davide-mariotti.github.io/apps/vigil">Website</a>
</p>

<p align="center">
  <a href="https://ko-fi.com/Z0X32ATNHL"><img src="https://ko-fi.com/img/githubbutton_sm.svg" alt="Buy me a coffee on Ko-fi"></a>
</p>

---

## English

### What it does

- Works out what's inside the PC by itself: CPU, graphics card, RAM sticks and slots, motherboard, disks. Nothing to set up.
- Puts the hottest temperature (CPU or GPU) in the tray, as a number: dark when all is well, amber within 10 degrees of your limit, red over it.
- Lists every sensor it finds, grouped by component: temperatures, fan speeds, power draw, load, clocks, voltages, each with its minimum and maximum since Vigil started.
- Draws the last 10 minutes of any sensor you click.
- Warns you when the CPU or GPU stays over your limit (90 °C by default) for more than a minute. Once per episode, not every second.
- Two modes, one exe. Started normally, it reads what Windows gives to anyone: CPU load, graphics card (temperature, power, fan), RAM, network. Started as administrator with the free PawnIO driver installed, it also reads CPU temperature and power, motherboard sensors and fans, disks.
- Starts with Windows if you ask it to. Turned on as administrator, it uses a scheduled task: full mode at every logon, no UAC prompt every morning.
- Speaks Italian or English, following the language of Windows.

### What it doesn't do

- It doesn't control fans. Yet: that's the next step, and it will come with a safety net (fans handed back to the BIOS when Vigil closes or crashes).
- It doesn't install drivers behind your back. Full mode needs PawnIO: Vigil tells you when it's missing and opens its official site, you install it.
- It doesn't connect to the internet. No telemetry, no auto-updates, no account. Settings live in `HKCU\SOFTWARE\VigilApp`, or in `Vigil.ini` in the portable version.
- It doesn't make up numbers. A sensor that only answers 0 without the driver is hidden, not shown as 0 °C.
- On many laptops it can't see everything: fans and some temperatures sit behind the maker's own firmware, each brand its own way. Vigil shows what the maker lets through.
- It doesn't run on macOS, Linux, or 32-bit and ARM Windows.
- It doesn't cool your PC. A can of compressed air still does that better.

### Download

1. Download [Vigil.exe](https://github.com/davide-mariotti/Vigil/raw/main/Vigil.exe) (2.9 MB). Windows 10 or 11, 64-bit, nothing else to install.
2. Put it in a folder that stays put (say `Documents\Tools`), not in Downloads.
3. Double-click it. The first time, SmartScreen may complain because the exe isn't signed: *More info* → *Run anyway*.
4. For every sensor: right-click the tray icon → *Restart as administrator*. If Vigil says PawnIO is missing, *Get PawnIO* opens [pawnio.eu](https://pawnio.eu/): install it, then *Installed: restart Vigil*.

**Portable version**: [Vigil-portable.zip](https://github.com/davide-mariotti/Vigil/raw/main/Vigil-portable.zip) (1.4 MB), the same exe with a `Vigil.ini` next to it. Unzip it anywhere, a USB stick too. As long as `Vigil.ini` sits next to the exe, the settings are saved in that file and the registry is never touched. That's also why this version can't start with Windows.

### How to use it

- The **number** in the notification area, next to the clock, is the hottest of CPU and GPU in °C. Hover it to see both. Before the first reading, or on a PC with no temperature to show, you see the Vigil icon instead.
- Double-click the icon for the window, right-click → *Exit* to close it. Closing the window leaves Vigil in the tray.
- Click a sensor to see its last 10 minutes at the bottom of the window.
- The settings (start with Windows, alert limit) sit at the bottom and are saved as you change them.

### How it works

Once a second Vigil:

1. asks every chip for its sensors through [LibreHardwareMonitorLib](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor), the library behind LibreHardwareMonitor and FanControl;
2. hides what isn't real: sensors stuck at 0 since the start (empty fan headers, unused network adapters), unplugged probes reading 127 °C, and, without the driver, whatever the CPU and motherboard would answer (zeros, and a Ryzen that swears every core runs at 1.55 V);
3. picks the main temperature of each chip, the package for the CPU and the core for the GPU, and puts the hottest in the tray. Hot spots run 10-20 degrees higher and would ring the alarm in every game;
4. if one stays over your limit for a minute, warns you, then waits for it to drop 5 degrees under before warning again.

Why administrator: CPU temperature, power draw and motherboard chips are read through a kernel driver, and Windows lets only administrators talk to drivers. Vigil uses [PawnIO](https://pawnio.eu/), the signed driver LibreHardwareMonitor and FanControl moved to after Windows Defender started flagging the old WinRing0.

### Build from source

No Visual Studio needed: Windows already ships the C# compiler of .NET Framework 4.

```cmd
build.bat
```

You get `Vigil.exe` and `Vigil-portable.zip` next to the sources. The libraries in `lib\` are packed inside the exe, and `build.bat` runs `Vigil.exe --selftest`, which checks the rules that decide what's shown and when the alarm rings. The icons come from `icon/make-icons.py` (Python, Pillow and Chrome), drawn with a [Lucide](https://lucide.dev) glyph.

### Support

If Vigil caught a dying fan before your CPU did, you can [buy me a coffee on Ko-fi](https://ko-fi.com/Z0X32ATNHL). That's how fan control gets written.

### License

[MIT](LICENSE). The libraries inside the exe keep their own licenses (MPL-2.0, Apache-2.0, MIT): the list, with where to get their source, is in [lib/README.md](lib/README.md).

---

## Italiano

### Cosa fa

- Capisce da solo cosa c'è nel PC: processore, scheda video, banchi di RAM e slot, scheda madre, dischi. Non c'è niente da configurare.
- Mette nella tray la temperatura più alta tra CPU e GPU, come numero: scuro se va tutto bene, arancione quando sei a 10 gradi dal tuo limite, rosso quando lo superi.
- Elenca tutti i sensori che trova, divisi per componente: temperature, giri delle ventole, consumi, carico, frequenze, tensioni, ognuno con il minimo e il massimo da quando Vigil è partito.
- Disegna gli ultimi 10 minuti del sensore su cui clicchi.
- Ti avvisa se CPU o GPU restano sopra il tuo limite (90 °C di base) per più di un minuto. Una volta per episodio, non ogni secondo.
- Due modalità, un solo exe. Aperto normalmente legge quello che Windows dà a chiunque: carico della CPU, scheda video (temperatura, consumo, ventola), RAM, rete. Aperto da amministratore, con il driver gratuito PawnIO installato, legge anche temperatura e consumo della CPU, sensori e ventole della scheda madre, dischi.
- Si avvia con Windows, se glielo chiedi. Se lo attivi da amministratore usa un'attività pianificata: modalità completa a ogni accensione, senza la conferma UAC ogni mattina.
- Parla italiano o inglese, in base alla lingua di Windows.

### Cosa non fa

- Non comanda le ventole. Per ora: è il prossimo passo, e arriverà con una rete di sicurezza (ventole restituite al BIOS quando Vigil si chiude o va in crash).
- Non installa driver di nascosto. La modalità completa vuole PawnIO: Vigil ti dice quando manca e ti apre il sito ufficiale, lo installi tu.
- Non si collega a internet. Niente telemetria, niente aggiornamenti automatici, niente account. Le impostazioni stanno in `HKCU\SOFTWARE\VigilApp`, o in `Vigil.ini` nella versione portable.
- Non si inventa i numeri. Un sensore che senza driver risponde solo 0 non compare, invece di segnare 0 °C.
- Su molti portatili non vede tutto: ventole e alcune temperature passano dal firmware del produttore, e ogni marca fa a modo suo. Vigil mostra quello che il produttore lascia passare.
- Non gira su macOS, Linux, né su Windows a 32 bit o ARM.
- Non raffredda il PC. Per quello una bomboletta d'aria compressa resta imbattibile.

### Scaricarlo

1. Scarica [Vigil.exe](https://github.com/davide-mariotti/Vigil/raw/main/Vigil.exe) (2,9 MB). Windows 10 o 11 a 64 bit, nient'altro da installare.
2. Mettilo in una cartella che non sposti (tipo `Documenti\Strumenti`), non in Download.
3. Doppio clic. La prima volta SmartScreen potrebbe lamentarsi perché l'exe non è firmato: *Ulteriori informazioni* → *Esegui comunque*.
4. Per avere tutti i sensori: tasto destro sull'icona nella tray → *Riavvia come amministratore*. Se Vigil ti dice che manca PawnIO, *Scarica PawnIO* apre [pawnio.eu](https://pawnio.eu/): lo installi, poi *Installato: riavvia Vigil*.

**Versione portable**: [Vigil-portable.zip](https://github.com/davide-mariotti/Vigil/raw/main/Vigil-portable.zip) (1,4 MB), lo stesso exe con accanto un `Vigil.ini`. Lo scompatti dove vuoi, anche su una chiavetta. Finché `Vigil.ini` sta vicino all'exe, le impostazioni si salvano in quel file e il registro non viene mai toccato. Per lo stesso motivo questa versione non si avvia con Windows.

### Come si usa

- Il **numero** nell'area di notifica, vicino all'orologio, è la temperatura più alta tra CPU e GPU, in °C. Passaci sopra col mouse per vederle tutte e due. Prima della prima lettura, o su un PC che non dà temperature, vedi l'icona di Vigil.
- Doppio clic sull'icona per la finestra, tasto destro → *Esci* per chiuderlo. Chiudere la finestra lascia Vigil nella tray.
- Clicca un sensore per vederne gli ultimi 10 minuti in fondo alla finestra.
- Le impostazioni (avvio con Windows, limite per gli avvisi) stanno in fondo e si salvano mentre le cambi.

### Come funziona

Una volta al secondo Vigil:

1. chiede a ogni chip i suoi sensori tramite [LibreHardwareMonitorLib](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor), la libreria che c'è dietro LibreHardwareMonitor e FanControl;
2. nasconde quello che non è vero: sensori fermi a 0 dall'avvio (attacchi per ventole vuoti, schede di rete inutilizzate), sonde scollegate che segnano 127 °C e, senza driver, quello che risponderebbero CPU e scheda madre (zeri, e un Ryzen convinto che ogni core giri a 1,55 V);
3. sceglie la temperatura principale di ogni chip, il package per la CPU e il core per la GPU, e mette la più alta nella tray. Gli hot spot stanno 10-20 gradi sopra e farebbero suonare l'allarme a ogni partita;
4. se una resta sopra il tuo limite per un minuto ti avvisa, poi aspetta che scenda di 5 gradi sotto il limite prima di avvisarti di nuovo.

Perché l'amministratore: temperatura e consumo della CPU e i chip della scheda madre si leggono con un driver di sistema, e Windows fa parlare con i driver solo gli amministratori. Vigil usa [PawnIO](https://pawnio.eu/), il driver firmato su cui sono passati LibreHardwareMonitor e FanControl da quando Windows Defender blocca il vecchio WinRing0.

### Compilarlo

Non serve Visual Studio: Windows ha già il compilatore C# di .NET Framework 4.

```cmd
build.bat
```

Ottieni `Vigil.exe` e `Vigil-portable.zip` accanto ai sorgenti. Le librerie in `lib\` finiscono dentro l'exe, e `build.bat` lancia `Vigil.exe --selftest`, che controlla le regole su cosa mostrare e quando far suonare l'allarme. Le icone le genera `icon/make-icons.py` (Python, Pillow e Chrome), con un glifo di [Lucide](https://lucide.dev).

### Supporto

Se Vigil ti ha fatto notare una ventola che moriva prima che se ne accorgesse la CPU, puoi [offrirmi un caffè su Ko-fi](https://ko-fi.com/Z0X32ATNHL). È così che arriva il controllo delle ventole.

### Licenza

[MIT](LICENSE). Le librerie dentro l'exe hanno le loro licenze (MPL-2.0, Apache-2.0, MIT): l'elenco, con dove trovarne i sorgenti, è in [lib/README.md](lib/README.md).
