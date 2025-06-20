
# CnCNet Tunnel Server

* .NET 9
* Cross platform (Windows, Linux, Mac, ...)
* No admin privileges required to run
* Supports CnCNet V2 & V3 tunnel protocol
* Supports CnCNet STUN protocol (for P2P clients)
* Supports IPv4 & IPv6

## How to run/install

* The V3 version requires the [.NET Runtime 9](https://dotnet.microsoft.com/en-us/download/dotnet/9.0/runtime).
* The V3+V2 version additionally requires the [ASP.NET Core Runtime 9](https://dotnet.microsoft.com/en-us/download/dotnet/9.0/runtime).

Make sure these ports are open/forwarded to the machine (default ports):

* TCP 50000 (V2)
* UDP 50000 (V2)
* UDP 50001 (V3)
* UDP 3478
* UDP 8054
* ICMP (for clients not using the built-in ping mechanism)

### Arguments

To see a list of possible arguments run:

```
cncnet-server -?
```

Example output:

```
Description:
  CnCNet tunnel server

Usage:
  cncnet-server [options] [[--] <additional arguments>...]]

Options:
  -?, -h, --help                                         Show help and usage information
  --version                                              Show version information
  -n, --name (REQUIRED)                                  Name of the server
  -p, --tunnel-port                                      Port used for the V3 tunnel server [default: 50001]
  -p2, --tunnel-v2-port                                  Port used for the V2 tunnel server [default: 50000]
  -m, --max-clients                                      Maximum clients allowed on the tunnel server [default: 200]
  -nm, --no-master-announce                              Don't register to master [default: False]
  -masp, --master-password                               Master password []
  -maip, --maintenance-password                          Maintenance password []
  -mu, --master-server-url                               Master server URL [default:
                                                         https://cncnet.org/api/v1/master-announce]
  -i, --ip-limit                                         Maximum clients allowed per IP address [default: 8]
  -np, --no-peer-to-peer                                 Disable STUN NAT traversal server (UDP 8054 & 3478) [default:
                                                         False]
  -3, --tunnel-v3-enabled                                Start a V3 tunnel server [default: True]
  -2, --tunnel-v2-enabled                                Start a V2 tunnel server [default: True]
  -sel, --server-log-level                               CnCNet server messages log level [default: Warning]
  <Critical|Debug|Error|Information|None|Trace|Warning>
  -syl, --system-log-level                               Low level system messages log level [default: Warning]
  <Critical|Debug|Error|Information|None|Trace|Warning>
  -6, --announce-ipv6                                    Announce IPv6 address to master server [default: True]
  -4, --announce-ipv4                                    Announce IPv4 address to master server [default: True]
  -h, --tunnel-v2-https                                  Use https Tunnel V2 web server [default: False]
  -mps, --max-packet-size                                Maximum accepted packet size [default: 2048]
  -mpg, --max-pings-global                               Maximum accepted ping requests globally [default: 1024]
  -mpi, --max-pings-per-ip                               Maximum accepted ping requests per IP [default: 20]
  -ai, -master-announce-interval                         Master server announce interval in seconds [default: 60]
  -c, --client-timeout                                   Client timeout in seconds [default: 60]

Additional Arguments:
  Arguments passed to the application that is being run.
```

### Start from console

```
cncnet-server --name NewServer
```

### Install as a service on Windows (using PowerShell)

```
Download <cncnet-server-win-x64.zip>
```

```
Extract to e.g. C:\cncnet-server\
```

```
New-Service -Name CnCNetServer -BinaryPathName '"C:\cncnet-server\cncnet-server.exe" --name "NewServer"' -StartupType "Automatic" -DisplayName "CnCNet Tunnel Server" -Description "CnCNet Tunnel Server"
```

```
Start-Service CnCNetServer
```

### Install as a daemon on Linux

```
sudo apt-get update && \
  sudo apt-get install -y aspnetcore-runtime-9.0
```

```
wget <cncnet-server-linux-x64.zip>
```

```
unzip -d cncnet-server <cncnet-server-linux-x64.zip>
```

```
useradd cncnet-server
```

```
passwd cncnet-server
```

```
chown cncnet-server -R /home/cncnet-server
```

```
cd /home/cncnet-server/
```

```
chmod +x cncnet-server
```

```
cd /etc/systemd/system/
```

```
vi cncnet-server.service
```

cncnet-server.service example contents:

```
[Unit]
Description=CnCNet Tunnel Server

[Service]
Type=notify
WorkingDirectory=/home/cncnet-server
ExecStart=/home/cncnet-server/cncnet-server --n "NewServer" --masp "PW" --maip "PW" --m 250
SyslogIdentifier=CnCNet-Server
User=cncnet-server
Restart=always
RestartSec=5

KillSignal=SIGINT
Environment=DOTNET_ENVIRONMENT=Production
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=DOTNET_PRINT_TELEMETRY_MESSAGE=false

[Install]
WantedBy=multi-user.target
```

```
sudo systemctl daemon-reload
sudo systemctl start cncnet-server.service
```

```
sudo ufw allow proto tcp from any to any port 50000
sudo ufw allow proto udp from any to any port 50000
sudo ufw allow proto udp from any to any port 50001
sudo ufw allow proto udp from any to any port 3478
sudo ufw allow proto udp from any to any port 8054
```

to start on machine start:

```
sudo systemctl enable cncnet-server.service
```

to inspect logs:

```
sudo journalctl -u cncnet-server
```
