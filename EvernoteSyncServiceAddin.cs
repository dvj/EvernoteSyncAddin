using System;
using System.Text;
using Gtk;
using Mono.Unix;


// Global TODOs:
// TODO - dont store password as plaintext (Gnome Keyring on linux, what about OSX, win?)
// TODO - prompt user for passwd if they dont store it
// TODO - support custom notebook name (or Evernote default) 

namespace Tomboy.Sync
{
    public class EvernoteSyncServiceAddin : SyncServiceAddin
    {
        private Entry _usernameEntry;
        private Entry _passwordEntry;
        private string _username;
        private string _password;
        private const string UsernameSetting = "/apps/tomboy/evernote/username";
        private const string PasswordSetting = "/apps/tomboy/evernote/password";
        
        private bool _initialized = false;

        /// <summary>
        /// Called as soon as Tomboy needs to do anything with the service
        /// </summary>
        public override void Initialize ()
        {
            _initialized = true;
        }

        public override void Shutdown ()
        {
            // Do nothing for now
        }

        public override bool Initialized {
            get {
                return _initialized;
            }
        }

        /// <summary>
        /// Creates a SyncServer instance that the SyncManager can use to
        /// synchronize with this service.  This method is called during
        /// every synchronization process.  If the same SyncServer object
        /// is returned here, it should be reset as if it were new.
        /// </summary>
        public override SyncServer CreateSyncServer ()
        {
            SyncServer server = new EvernoteSyncServer();
            return server;
        }

        public override void PostSyncCleanup ()
        {
            // Nothing to do
        }

        /// <summary>
        /// Creates a Gtk.Widget that's used to configure the service.  This
        /// will be used in the Synchronization Preferences.  Preferences should
        /// not automatically be saved by a GConf Property Editor.  Preferences
        /// should be saved when SaveConfiguration () is called.
        /// </summary>
        public override Widget CreatePreferencesControl ()
        {
            Table table = new Table (1, 3, false) {RowSpacing = 5, ColumnSpacing = 10};

            // Read settings out of gconf
            string username;
            string password;
            if (GetConfigSettings(out username, out password) == false)
            {
                username = string.Empty;
                password = string.Empty;
            }

            Label usernameL = new Label(Catalog.GetString("_Username:"));
            Label passwordL = new Label(Catalog.GetString("_Password:"));
            usernameL.Xalign = 1;
            table.Attach (usernameL, 0, 1, 0, 1,
                          AttachOptions.Fill,
                          AttachOptions.Expand | AttachOptions.Fill,
                          0, 0);
            table.Attach(passwordL, 0, 1, 1, 2,
                                      AttachOptions.Fill,
                                      AttachOptions.Expand | AttachOptions.Fill,
                                      0, 0);
            _usernameEntry = new Entry {Text = username};
            table.Attach(_usernameEntry, 1, 2, 0, 1,
                          AttachOptions.Expand | AttachOptions.Fill,
                          AttachOptions.Expand | AttachOptions.Fill,
                          0, 0);
            usernameL.MnemonicWidget = _usernameEntry;

            _passwordEntry = new Entry {Text = password, Visibility = false};
            table.Attach(_passwordEntry, 1, 2, 1, 2,
                          AttachOptions.Expand | AttachOptions.Fill,
                          AttachOptions.Expand | AttachOptions.Fill,
                          0, 0);
            passwordL.MnemonicWidget = _passwordEntry;

            table.ShowAll ();
            return table;
        }

        /// <summary>
        /// The Addin should verify and check the connection to the service
        /// when this is called.  If verification and connection is successful,
        /// the addin should save the configuration and return true.
        /// </summary>
        public override bool SaveConfiguration ()
        {
            string syncUsername = _usernameEntry.Text.Trim ();

            if (syncUsername == string.Empty) {
                // TODO: Figure out a way to send the error back to the client
                Logger.Debug ("The username is empty");
                throw new TomboySyncException (Catalog.GetString ("Username field is empty."));
            }

            _username = syncUsername;
            _password = _passwordEntry.Text.Trim();
            Preferences.Set(UsernameSetting, _username);
            string password64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(_password));
            Preferences.Set(PasswordSetting, password64);
            return true;
        }

        /// <summary>
        /// Reset the configuration so that IsConfigured will return false.
        /// </summary>
        public override void ResetConfiguration ()
        {
            Preferences.Set(UsernameSetting, string.Empty);
            Preferences.Set(PasswordSetting, string.Empty);
        }

        /// <summary>
        /// Returns whether the addin is configured enough to actually be used.
        /// </summary>
        public override bool IsConfigured
        {
            get {
                string username = Preferences.Get(UsernameSetting) as String;

                if (!string.IsNullOrEmpty(username)) {
                    return true;
                }

                return false;
            }
        }

        /// <summary>
        /// The name that will be shown in the preferences to distinguish
        /// between this and other SyncServiceAddins.
        /// </summary>
        public override string Name
        {
            get {
                return Catalog.GetString ("Evernote");
            }
        }

        /// <summary>
        /// Specifies a unique identifier for this addin.  This will be used to
        /// set the service in preferences.
        /// Note: This is just a random guid, it has no special meaning to anyone
        /// </summary>
        public override string Id
        {
            get {
                return "evernote-3AB63953-7C9B-47c5-81C9-2383F7528E36";
            }
        }

        /// <summary>
        /// Returns true if the addin has all the supporting libraries installed
        /// on the machine or false if the proper environment is not available.
        /// If false, the preferences dialog will still call
        /// CreatePreferencesControl () when the service is selected.  It's up
        /// to the addin to present the user with what they should install/do so
        /// IsSupported will be true.
        /// </summary>
        /// TODO - if we extend the service to require the html plugin, add a check here
        /// TODO - should also check ahead of time that they have the thrift dll.
        public override bool IsSupported
        {
            get {
                return true;
            }
        }

        #region Private Methods
        /// <summary>
        /// Get config settings
        /// </summary>
        public static bool GetConfigSettings (out string username, out string password)
        {
            username = Preferences.Get(UsernameSetting) as String;
            string password64 = Preferences.Get(PasswordSetting) as String;
            if (!string.IsNullOrEmpty(password64))
            {
                password = Encoding.UTF8.GetString(Convert.FromBase64String(password64));
            }
            else
            {
                password = "";
            }
            if (!string.IsNullOrEmpty(username))
            {
                return true;
            }

            return false;
        }
        #endregion // Private Methods
    }
}
