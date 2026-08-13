# Avtoforward Agent for Ubuntu

Headless-агент для подключения Ubuntu-компьютера к Autof. Агент регистрируется в API, постоянно поддерживает Socket.IO-соединение и принимает задания на печать чеков, этикеток и оплату через PrivatBank POS-терминал.

## Быстрый запуск

### 1. Установить зависимости

Для работы уже опубликованного агента .NET устанавливать не нужно: пакет собирается как self-contained приложение.

```bash
sudo apt update
sudo apt install -y cups cups-client imagemagick
sudo systemctl enable --now cups
```

- `cups` и `cups-client` нужны для печати через CUPS;
- `imagemagick` нужен для логотипов, изображений и растрового текста;
- если используются только прямое USB-устройство или TCP-принтер, CUPS необязателен.

Для сборки проекта из исходников дополнительно нужен .NET SDK 10.

### 2. Собрать пакет

Из корня репозитория:

```bash
./scripts/publish-ubuntu.sh linux-x64 0.1.0-local
```

Готовый self-contained пакет появится в `artifacts/publish/linux-x64`. Если пакет получен из GitHub Actions, этот шаг можно пропустить.

### 3. Установить агент

```bash
sudo ./scripts/install-ubuntu-agent.sh ./artifacts/publish/linux-x64
```

Скрипт установки:

- копирует приложение в `/opt/avtoforward-agent`;
- создаёт системного пользователя `avtoforward-agent`;
- создаёт конфигурацию `/etc/avtoforward-agent/agentsettings.json`;
- создаёт каталог состояния `/var/lib/avtoforward-agent`;
- устанавливает сервис `avtoforward-agent.service`;
- включает таймер автоматических обновлений.

Повторная установка обновляет приложение, но сохраняет существующую конфигурацию и зарегистрированное устройство.

### 4. Настроить подключение

Открыть конфигурацию:

```bash
sudo nano /etc/avtoforward-agent/agentsettings.json
```

Минимально необходимо заполнить:

```json
{
  "agentName": "ubuntu-warehouse-01",
  "reportedUserName": "warehouse",
  "apiBaseUrl": "https://api.autof.com.ua",
  "socketIoUrl": "",
  "registrationToken": "CHANGE_ME"
}
```

- `agentName` — уникальное имя агента, которое будет видно в CRM;
- `reportedUserName` — имя пользователя или рабочего места;
- `apiBaseUrl` — для production используется `https://api.autof.com.ua`;
- `socketIoUrl` — обычно оставить пустым, адрес вернёт API при регистрации;
- `registrationToken` — значение серверного `WORKSTATION_AGENT_REGISTRATION_TOKEN`. Ключ нужно получить у администратора API и не добавлять в Git.

Остальные поля из созданного `agentsettings.json` удалять не нужно. Примеры настройки периферии приведены ниже.

### 5. Проверить конфигурацию и запустить

```bash
sudo -u avtoforward-agent \
  /opt/avtoforward-agent/WorkstationAgent.Ubuntu \
  --validate \
  --config /etc/avtoforward-agent/agentsettings.json \
  --state /var/lib/avtoforward-agent/state.json

sudo systemctl enable --now avtoforward-agent
sudo systemctl status avtoforward-agent --no-pager
sudo journalctl -u avtoforward-agent -n 50 --no-pager
```

При первом запуске агент выполнит `POST /workstation-agent/register`. Полученные `deviceId`, `apiKey`, имя и Socket.IO URL будут сохранены в `/var/lib/avtoforward-agent/state.json` с правами `0600`.

В успешном журнале должны появиться сообщения:

```text
Registration completed
Socket.IO transport connected
Agent connection accepted
```

После этого агент появится в списке онлайн-агентов CRM. Если список был открыт до запуска, обновите страницу.

## Настройка принтера чеков

### CUPS

Посмотреть имена доступных очередей:

```bash
lpstat -a
lpstat -p -d
```

Пример блока `receiptPrinter`:

```json
"receiptPrinter": {
  "enabled": true,
  "transportMode": "cups",
  "printerName": "XP-80",
  "devicePath": "/dev/usb/lp0",
  "host": "192.168.1.50",
  "port": 9100,
  "connectTimeoutSeconds": 10,
  "characterEncoding": "cp866",
  "feedLinesAfterPrint": 4,
  "maxImageWidthDots": 576
}
```

`printerName` должен полностью совпадать с именем из `lpstat -a`. Для 58-мм принтера обычно используется `maxImageWidthDots: 384`, для 80-мм — `576`.

### Прямое USB-устройство

Проверить путь:

```bash
ls -l /dev/usb/lp*
```

Изменить транспорт:

```json
"enabled": true,
"transportMode": "device",
"devicePath": "/dev/usb/lp0"
```

Установщик добавляет пользователя агента в группу `lp`. Если запись в устройство запрещена, требуется udev-правило для конкретного USB VID/PID принтера.

### TCP-принтер

```json
"enabled": true,
"transportMode": "tcp",
"host": "192.168.1.50",
"port": 9100
```

Проверка сети:

```bash
nc -vz 192.168.1.50 9100
```

## Настройка принтера этикеток

Пример CUPS-конфигурации этикетки 58 × 40 мм с зазором 2 мм:

```json
"labelPrinter": {
  "enabled": true,
  "transportMode": "cups",
  "printerName": "TSC-Label",
  "devicePath": "/dev/usb/lp1",
  "host": "192.168.1.51",
  "port": 9100,
  "connectTimeoutSeconds": 10,
  "characterEncoding": "ascii",
  "labelWidthMm": 58,
  "labelHeightMm": 40,
  "gapMm": 2,
  "direction": 0,
  "speed": 2,
  "density": 8,
  "codePage": null
}
```

Обязательно укажите фактические `labelWidthMm`, `labelHeightMm` и `gapMm`. Эти значения используются при формировании TSPL-команд `SIZE` и `GAP`. Для прямого USB или TCP поменяйте `transportMode` так же, как для принтера чеков.

## Настройка PrivatBank POS-терминала

Терминал должен находиться в одной доступной сети с Ubuntu-компьютером и принимать PrivatBank JSON-протокол по TCP.

```json
"posTerminal": {
  "enabled": true,
  "host": "192.168.0.110",
  "port": 2000,
  "merchantId": "1",
  "timeoutSeconds": 180
}
```

Укажите фактический IP терминала. Желательно закрепить его в DHCP, иначе после смены адреса терминал перестанет получать сумму.

Проверить порт и служебный `PingDevice` без проведения оплаты:

```bash
nc -vz 192.168.0.110 2000

sudo -u avtoforward-agent \
  /opt/avtoforward-agent/WorkstationAgent.Ubuntu \
  --pos-test \
  --config /etc/avtoforward-agent/agentsettings.json \
  --state /var/lib/avtoforward-agent/state.json
```

Успешный ответ содержит `"status":"approved"` и `"responseCode":"0000"`. Команда `--pos-test` не создаёт платёжную транзакцию.

## Тестовая печать

Внимание: следующие команды физически печатают тестовый документ.

```bash
sudo -u avtoforward-agent \
  /opt/avtoforward-agent/WorkstationAgent.Ubuntu \
  --print-test receipt \
  --config /etc/avtoforward-agent/agentsettings.json \
  --state /var/lib/avtoforward-agent/state.json

sudo -u avtoforward-agent \
  /opt/avtoforward-agent/WorkstationAgent.Ubuntu \
  --print-test label \
  --config /etc/avtoforward-agent/agentsettings.json \
  --state /var/lib/avtoforward-agent/state.json
```

После изменения конфигурации перезапустите сервис:

```bash
sudo systemctl restart avtoforward-agent
sudo journalctl -u avtoforward-agent -f
```

## Повторная регистрация

Обычные изменения принтеров или POS требуют только перезапуска сервиса. Если изменились `agentName`, домен API или ключ регистрации, сохраните старое состояние и зарегистрируйте агент заново:

```bash
sudo systemctl stop avtoforward-agent
sudo mv /var/lib/avtoforward-agent/state.json /var/lib/avtoforward-agent/state.json.backup
sudo systemctl start avtoforward-agent
sudo journalctl -u avtoforward-agent -f
```

Если новая регистрация не удалась, остановите сервис и верните `state.json.backup` на место.

## Автоматические обновления

Параметры по умолчанию:

```json
"autoUpdateEnabled": true,
"updateChannel": "main"
```

Каждый push в `main` публикует подписанный `linux-x64`-пакет в Autof API. Systemd-таймер проверяет обновления каждые пять минут, проверяет RSA-PSS подпись, размер и SHA-256, сохраняет предыдущий бинарник и перезапускает агент.

Проверка таймера и последнего обновления:

```bash
systemctl list-timers avtoforward-agent-update.timer
sudo systemctl status avtoforward-agent-update.timer --no-pager
sudo journalctl -u avtoforward-agent-update.service -n 50 --no-pager
```

## Диагностика

### Агент не появляется в CRM

```bash
sudo systemctl status avtoforward-agent --no-pager
sudo journalctl -u avtoforward-agent -n 100 --no-pager
getent hosts api.autof.com.ua
```

Проверьте `apiBaseUrl`, `registrationToken`, уникальность `agentName`, системное время и доступ к HTTPS/Socket.IO.

### Принтер не печатает

```bash
lpstat -a
ls -l /dev/usb/lp*
id avtoforward-agent
```

Проверьте `enabled`, правильность `transportMode`, имя CUPS-очереди, USB-права или доступность TCP-порта.

### POS не получает сумму

```bash
nc -vz TERMINAL_IP 2000
sudo journalctl -u avtoforward-agent -f
```

Проверьте `posTerminal.enabled`, актуальный IP, порт и `merchantId`. В журнале при нажатии кнопки оплаты должно появиться событие `pos:terminal:purchase`.

## Разработка и тесты

Проект использует .NET 10:

```bash
dotnet restore WorkstationAgent.Ubuntu.Tests/WorkstationAgent.Ubuntu.Tests.csproj \
  --configfile WorkstationAgent/NuGet.Config
dotnet build WorkstationAgent.Ubuntu/WorkstationAgent.Ubuntu.csproj -c Release
dotnet test WorkstationAgent.Ubuntu.Tests/WorkstationAgent.Ubuntu.Tests.csproj -c Release
```

Тесты проверяют ESC/POS и TSPL, CUPS/device/TCP-транспорты, JSON-контракты с API и безопасную установку подписанных обновлений. Финальная проверка конкретного принтера всё равно должна учитывать USB-права, прошивку, калибровку носителя, резак и плотность печати.
