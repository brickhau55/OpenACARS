using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using VAFlightTracker.Core;
using VAFlightTracker.Models;

namespace VAFlightTracker
{
    public partial class MainWindow : Window
    {

        // STATE
        private FlightState currentFlightState = FlightState.PreFlight;
        private FlightPhase currentPhase = FlightPhase.Preflight;

        // TELEMETRY
        private TelemetryData lastTelemetry;

        // TIMERS
        private DateTime cruiseCandidateTime;
        private DateTime descentCandidateTime;
        private DateTime blockCandidateTime;
        private DateTime lastUIUpdate = DateTime.MinValue;

        public List<Airport> AllAirports = new List<Airport>();
        private XPlaneListener listener;
        private bool isConnected = false;
        private bool flightActive = false;
        private string? currentFlightFile;
        private bool taxiDetected = false;
        private bool takeoffDetected = false;
        private bool cruiseDetected = false;
        private bool descentDetected = false;
        private bool landingDetected = false;
        private bool blockInDetected = false;
        private bool pushbackDetected = false;

        private double lastVerticalSpeed = 0;
        private double landingRate = 0;

        private string FormatAltitude(int altitude)
        {
            if (altitude >= 18000)
                return $"FL{altitude / 100}";
            else
                return $"{altitude} ft";
        }

        public MainWindow()
        {

            SetFlightFieldsLocked(false);

            InitializeComponent();

            NetworkComboBox.SelectedIndex = 0;

            LoadAirports();

#if !DEBUG
DebugTextBox.Visibility = Visibility.Collapsed;
#endif


            listener = new XPlaneListener();

            listener.OnDataReceived += Listener_OnDataReceived;
            listener.OnTelemetry += Listener_OnTelemetry;

            listener.Start();
        }


        private void DepartureTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string search = DepartureTextBox.Text.ToUpper();

            var filtered = AllAirports
                .Where(a =>
                    (!string.IsNullOrEmpty(a.ICAO) && a.ICAO.StartsWith(search)) ||
                    (!string.IsNullOrEmpty(a.IATA) && a.IATA.StartsWith(search)))
                .Take(50)
                .ToList();

            DepartureComboBox.ItemsSource = filtered;

            var exactMatch = filtered.FirstOrDefault(a =>
            a.ICAO == search || a.IATA == search);

            if (exactMatch != null)
                DepartureComboBox.SelectedItem = exactMatch;
            else
                DepartureComboBox.SelectedItem = null;

        }

        private void DepartureComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DepartureComboBox.SelectedItem is Airport airport)
                DepartureTextBox.Text = airport.ICAO;
        }

        private void ArrivalTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string search = ArrivalTextBox.Text.ToUpper();

            var filtered = AllAirports
                .Where(a =>
                    (!string.IsNullOrEmpty(a.ICAO) && a.ICAO.StartsWith(search)) ||
                    (!string.IsNullOrEmpty(a.IATA) && a.IATA.StartsWith(search)))
                .Take(50)
                .ToList();

            ArrivalComboBox.ItemsSource = filtered;

            var exactMatch = filtered.FirstOrDefault(a =>
            a.ICAO == search || a.IATA == search);

            if (exactMatch != null)
                ArrivalComboBox.SelectedItem = exactMatch;
            else
                ArrivalComboBox.SelectedItem = null;
        }

        private void ArrivalComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!ArrivalComboBox.IsDropDownOpen)
                return;

            if (ArrivalComboBox.SelectedItem is Airport airport)
                ArrivalTextBox.Text = airport.ICAO;
        }

        private void AlternateTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string search = AlternateTextBox.Text.ToUpper();

            var filtered = AllAirports
                .Where(a =>
                    (!string.IsNullOrEmpty(a.ICAO) && a.ICAO.StartsWith(search)) ||
                    (!string.IsNullOrEmpty(a.IATA) && a.IATA.StartsWith(search)))
                .Take(50)
                .ToList();

            AlternateComboBox.ItemsSource = filtered;

            var exactMatch = filtered.FirstOrDefault(a =>
            a.ICAO == search || a.IATA == search);

            if (exactMatch != null)
                AlternateComboBox.SelectedItem = exactMatch;
            else
                AlternateComboBox.SelectedItem = null;
        }

        private void AlternateComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!AlternateComboBox.IsDropDownOpen)
                return;

            if (AlternateComboBox.SelectedItem is Airport airport)
                AlternateTextBox.Text = airport.ICAO;
        }

        private void Listener_OnDataReceived(string data)
        {
#if DEBUG
            Dispatcher.BeginInvoke(new Action(() =>
            {
                DebugTextBox.AppendText(data + Environment.NewLine);

                if (DebugTextBox.LineCount > 200)
                {
                    DebugTextBox.Clear();
                }

                DebugTextBox.ScrollToEnd();
            }));
#endif
        }

        private void Listener_OnTelemetry(TelemetryData t)
        {
            lastTelemetry = t;

            if ((DateTime.UtcNow - lastUIUpdate).TotalSeconds > 1)
            {
                Dispatcher.Invoke(() =>
                {
                    DebugTextBox.AppendText(
                        $"GS:{t.GroundSpeed:F0} ALT:{t.Altitude:F0} VS:{t.VerticalSpeed:F0}\n");

                    DebugTextBox.ScrollToEnd();
                });

                lastUIUpdate = DateTime.UtcNow;
            }

            if (!flightActive)
                return;

            landingRate = lastVerticalSpeed;
            lastVerticalSpeed = t.VerticalSpeed;

            DetectFlightEvents(t);
        }

        private void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            isConnected = true;

            ConnectionStatusText.Text = "Connected";
            ConnectionStatusText.Foreground = System.Windows.Media.Brushes.Green;

            MessageBox.Show("Connected to VA Server.");

            StartFlightButton.IsEnabled = true;
        }

        private void StartFlightButton_Click(object sender, RoutedEventArgs e)
        {
            switch (currentFlightState)
            {
                case FlightState.PreFlight:
                    StartFlight();
                    break;

                case FlightState.InFlight:
                    EndFlight();
                    break;

                case FlightState.PostFlight:
                    FilePirep();
                    break;
            }
        }
        private void StartFlight()
        {
            if (!ValidateFlight())
                return;

            if (!isConnected)
            {
                MessageBox.Show("Please connect first.");
                return;
            }

            if (lastTelemetry.GroundSpeed > 10)
            {
                MessageBox.Show("Aircraft must be stationary to start the flight.");
                return;
            }

            if (Math.Abs(lastTelemetry.VerticalSpeed) > 200)
            {
                MessageBox.Show("Flight cannot be started while airborne.");
                return;
            }

            // Create flight log
            Directory.CreateDirectory("Flights");

            string flightNumber = FlightNumberTextBox.Text;

            string filename =
                $"Flights/{flightNumber}_{DateTime.UtcNow:yyyy_MM_dd_HH-mm}.txt";

            File.WriteAllText(filename,
            $"Flight started: {DateTime.UtcNow:yyyy-MM-dd HH:mm}Z\n");

            currentFlightFile = filename;

            LogEvent("ACARS LOG STARTED");
            LogEvent($"FLIGHT {FlightNumberTextBox.Text} {DepartureTextBox.Text}-{ArrivalTextBox.Text}");

            // Reset flight detection
            pushbackDetected = false;
            taxiDetected = false;
            takeoffDetected = false;
            cruiseDetected = false;
            descentDetected = false;
            landingDetected = false;
            blockInDetected = false;

            flightActive = true;

            // Lock flight plan fields
            SetFlightFieldsLocked(true);

            currentFlightState = FlightState.InFlight;

            StartFlightButton.Content = "End Flight";

            MessageBox.Show("Flight logging started.");
        }

        private void EndFlight()
        {
            if (lastTelemetry.GroundSpeed > 3)
            {
                MessageBox.Show("Aircraft must be stationary to end the flight.");
                return;
            }

            LogEvent("FLIGHT ENDED");

            flightActive = false;

            currentFlightState = FlightState.PostFlight;

            StartFlightButton.Content = "File PIREP";
        }
        private void FilePirep()
        {
            MessageBox.Show("PIREP filing will be implemented later.");

            SetFlightFieldsLocked(false);

            currentFlightState = FlightState.PreFlight;

            StartFlightButton.Content = "Start Flight";
        }

        private bool ValidateFlight()
        {
            if (NetworkComboBox.SelectedItem == null)
            {
                MessageBox.Show("Please select a Network.");
                return false;
            }
            if (string.IsNullOrWhiteSpace(FlightNumberTextBox.Text))
            {
                MessageBox.Show("Please enter a Flight Number.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(LegTextBox.Text))
            {
                MessageBox.Show("Please enter the flight Leg.");
                return false;
            }

            if (AircraftComboBox.SelectedItem == null)
            {
                MessageBox.Show("Please select an Aircraft.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(DepartureTextBox.Text))
            {
                MessageBox.Show("Please enter a Departure airport.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(ArrivalTextBox.Text))
            {
                MessageBox.Show("Please enter an Arrival airport.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(AlternateTextBox.Text))
            {
                MessageBox.Show("Please enter an Alternate airport.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(RouteTextBox.Text))
            {
                MessageBox.Show("Please enter a Route.");
                return false;
            }

            return true;
        }

        private void LogEvent(string message)
        {
            string logLine = $"{DateTime.UtcNow:HH:mm:ss}Z - {message}";
            if (currentFlightFile != null)
            {
                File.AppendAllText(currentFlightFile, logLine + Environment.NewLine);
            }
        }

        private void DetectFlightEvents(TelemetryData t)
        {
            if (currentFlightFile == null)
                return;

            double gs = t.GroundSpeed;
            double alt = t.Altitude;
            double vs = t.VerticalSpeed;
            // PUSHBACK
            if (!pushbackDetected && gs > 2 && gs < 5)
            {
                LogEvent("PUSHBACK");
                pushbackDetected = true;
            }

            // TAXI OUT
            if (!taxiDetected && gs >= 5 && t.OnGround)
            {
                LogEvent("TAXI OUT");
                taxiDetected = true;
                currentPhase = FlightPhase.TaxiOut;
            }

            // TAKEOFF
            if (!takeoffDetected && alt > 100 && vs > 500)
            {
                LogEvent("TAKEOFF");
                takeoffDetected = true;
            }

            // TOP OF CLIMB / CRUISE DETECTION
            if (takeoffDetected && !cruiseDetected && alt > 3000)
            {
                if (Math.Abs(vs) < 300)
                {
                    if (cruiseCandidateTime == DateTime.MinValue)
                        cruiseCandidateTime = DateTime.UtcNow;

                    if ((DateTime.UtcNow - cruiseCandidateTime).TotalSeconds > 20)
                    {
                        LogEvent($"TOP OF CLIMB {FormatAltitude((int)alt)}");
                        cruiseDetected = true;
                    }
                }
                else
                {
                    cruiseCandidateTime = DateTime.MinValue;
                }
            }

            // TOP OF DESCENT
            if (cruiseDetected && !descentDetected && vs < -800 && alt > 10000)
            {
                LogEvent("TOP OF DESCENT");
                descentDetected = true;
            }

            // LANDING
            if (!landingDetected && alt < 10 && lastVerticalSpeed < -50 && gs > 40)
            {
                LogEvent($"TOUCHDOWN VS: {landingRate:F0} fpm");
                landingDetected = true;
            }

            // BLOCK IN
            if (landingDetected && !blockInDetected && gs < 3 && alt < 5)
            {
                LogEvent("BLOCK IN");
                blockInDetected = true;
                flightActive = false;

                Dispatcher.Invoke(() =>
                {
                    currentFlightState = FlightState.PostFlight;
                    StartFlightButton.Content = "File PIREP";
                });
            }
        }
        private void LoadAirports()
        {
            string path = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "Data/us_airports.csv");

            if (!File.Exists(path))
                return;

            foreach (var line in File.ReadLines(path))
            {
                var parts = line.Split(',');

                if (parts.Length < 3)
                    continue;

                Airport airport = new Airport
                {
                    ICAO = parts[0],
                    IATA = parts[1],
                    Name = parts[2]
                };

                AllAirports.Add(airport);
            }

            DepartureComboBox.ItemsSource = CollectionViewSource.GetDefaultView(AllAirports);
            ArrivalComboBox.ItemsSource = CollectionViewSource.GetDefaultView(AllAirports);
            AlternateComboBox.ItemsSource = CollectionViewSource.GetDefaultView(AllAirports);

        }
        private void DebugTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
        }

        private void SetFlightFieldsLocked(bool locked)
        {
            if (NetworkComboBox != null) NetworkComboBox.IsEnabled = !locked;
            if (FlightNumberTextBox != null) FlightNumberTextBox.IsEnabled = !locked;
            if (LegTextBox != null) LegTextBox.IsEnabled = !locked;
            if (AircraftComboBox != null) AircraftComboBox.IsEnabled = !locked;

            if (DepartureTextBox != null) DepartureTextBox.IsEnabled = !locked;
            if (DepartureComboBox != null) DepartureComboBox.IsEnabled = !locked;

            if (ArrivalTextBox != null) ArrivalTextBox.IsEnabled = !locked;
            if (ArrivalComboBox != null) ArrivalComboBox.IsEnabled = !locked;

            if (AlternateTextBox != null) AlternateTextBox.IsEnabled = !locked;
            if (AlternateComboBox != null) AlternateComboBox.IsEnabled = !locked;

            if (RouteTextBox != null) RouteTextBox.IsEnabled = !locked;
        }
    }
}