﻿using SICore.Connections;
using SICore.Network.Contracts;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using R = SICore.Network.Properties.Resources;

namespace SICore.Network.Servers
{
    /// <summary>
    /// Класс, имитирующий работу сервера. Обеспечивает пересылку сообщений между клиентами
    /// </summary>
    public abstract class Server : IServer
    {
        /// <summary>
        /// Список доступных клиентов
        /// </summary>
        protected List<IClient> _clients = new List<IClient>();
        protected object _clientsSync = new object();

        public object ClientsSync => _clientsSync;

        protected object _connectionsSync = new object();

        public object ConnectionsSync => _connectionsSync;

        /// <summary>
        /// Является ли сервер главным
        /// </summary>
        public abstract bool IsMain { get; }

        public event Action<Exception, bool> Error;

        public void OnError(Exception exc, bool isWarning) => Error?.Invoke(exc, isWarning);

        protected abstract IEnumerable<IConnection> Connections { get; }

        public virtual bool AddConnection(IConnection connection)
        {
            connection.MessageReceived += Connection_MessageReceived;
            connection.ConnectionClose += Connection_ConnectionClosed;
            connection.Error += OnError;

            return true;
        }

        public virtual void RemoveConnection(IConnection connection, bool withError)
        {
			ClearListeners(connection);
            connection.Dispose();

			ConnectionClosed?.Invoke(withError);
		}

		protected void ClearListeners(IConnection connection)
		{
			connection.MessageReceived -= Connection_MessageReceived;
			connection.ConnectionClose -= Connection_ConnectionClosed;
			connection.Error -= OnError;
		}

		protected readonly INetworkLocalizer _localizer;

        public Server(INetworkLocalizer localizer)
        {
			_localizer = localizer;
        }

        public event Action<bool> ConnectionClosed;

        protected void Connection_ConnectionClosed(IConnection connection, bool withError)
        {
			try
			{
				RemoveConnection(connection, withError);
			}
			catch (Exception exc)
			{
				OnError(exc, false);
			}

			if (IsMain)
            {
				string clientName = null;
				lock (connection.ClientsSync)
				{
					if (connection.Clients.Count > 0)
					{
						clientName = connection.Clients[0];
					}
				}

				if (clientName != null)
				{
                    var m = new Message(string.Join(Message.ArgsSeparator, SystemMessages.Disconnect, clientName, (withError ? "+" : "-")), "", Constants.GameName);
                    ProcessOutgoingMessage(m);
				}
            }
            else
            {
				lock (_clientsSync)
                {
					foreach (var client in _clients)
					{
						var m = new Message(SystemMessages.Disconnect, "", client.Name);
                        ProcessOutgoingMessage(m);
                    }
                }
            }
        }

        protected event Action<Message> MessageReceived;

        private const char AnonymousSenderPrefix = '\n';

        /// <summary>
        /// Получено сообщение от внешнего сервера
        /// </summary>
        /// <param name="connection">Сервер, от которого пришло сообщение</param>
        /// <param name="m">Присланное сообщение</param>
        private void Connection_MessageReceived(IConnection connection, Message m)
        {
            try
            {
                
                string sender = m.Sender, receiver = m.Receiver;
                if (string.IsNullOrEmpty(receiver))
                    receiver = IsMain ? Constants.GameName : Constants.Everybody;

                var emptySender = string.IsNullOrEmpty(sender);

                if (emptySender)
                {
                    if (!IsMain)
                    {
                        OnError(new Exception(_localizer[nameof(R.UnknownSenderMessage)] + ": " + m.Text), true);
                        return;
                    }

                    sender = AnonymousSenderPrefix + connection.Id;
                }
                else
                {
                    lock (connection.ClientsSync)
                    {
                        if (sender != Constants.GameName && !connection.Clients.Contains(sender))
                        {
                            return; // Защита от подлога
                        }
                    }
                }

                if (sender != m.Sender || receiver != m.Receiver)
                {
                    m = new Message(m.Text, sender, receiver, m.IsSystem, m.IsPrivate);
                }

                ProcessIncomingMessage(m);
            }
            catch (Exception exc)
            {
                OnError(new Exception("Message: " + m.Text, exc), false);
            }
        }

        private void ProcessIncomingMessage(Message message)
        {
            lock (_clientsSync)
            {
                foreach (var client in _clients)
                {
                    if (message.Receiver == client.Name || message.Receiver == Constants.Everybody || client.Name == "" || !message.IsSystem && !message.IsPrivate)
                    {
                        client.AddIncomingMessage(message);
                    }
                }
            }

            if (IsMain)
            {
                // Надо переслать это сообщение остальным
                lock (_connectionsSync)
                {
                    foreach (var connection in Connections)
                    {
                        bool send;

                        if (IsMain)
                        {
                            send = (connection.UserName != message.Sender)
                                && ((connection.UserName == message.Receiver) || message.Receiver == Constants.Everybody && connection.IsAuthenticated);
                        }
                        else
                        {
                            lock (connection.ClientsSync)
                            {
                                send = !connection.Clients.Contains(message.Sender)
                                    && (connection.Clients.Contains(message.Receiver) || message.Receiver == Constants.Everybody && connection.IsAuthenticated);
                            }
                        }

                        if (send)
                        {
                            connection.SendMessage(message);
                        }
                    }
                }
            }
        }

        private void ProcessOutgoingMessage(Message message)
        {
            lock (_clientsSync)
            {
                foreach (var client in _clients)
                {
                    if ((message.Receiver == client.Name || client.Name.Length == 0 || message.Receiver == Constants.Everybody || !message.IsSystem && !message.IsPrivate) && client.Name != message.Sender)
                        client.AddIncomingMessage(message);
                }
            }

            lock (_connectionsSync)
            {
                foreach (var connection in Connections)
                {
                    bool send;

                    if (IsMain)
                    {
                        send = (connection.UserName != message.Sender)
                            && (message.Receiver == Constants.Everybody && connection.IsAuthenticated || (connection.UserName == message.Receiver));
                    }
                    else
                    {
                        lock (connection.ClientsSync)
                        {
                            send = !connection.Clients.Contains(message.Sender) &&
                                (message.Receiver == Constants.Everybody && connection.IsAuthenticated || connection.Clients.Contains(message.Receiver));
                        }
                    }

                    if (send)
                    {
                        connection.SendMessage(message);
                    }
                }
            }
        }

        public bool Contains(string name)
        {
            lock (_clientsSync)
            {
                return _clients.Exists(c => c.Name == name);
            }
        }

        /// <summary>
        /// Добавление нового клиента
        /// </summary>
        /// <param name="client">Добавляемый клиент</param>
        public void AddClient(IClient client)
        {
			lock (_clientsSync)
			{
				if (_clients.Contains(client))
					return;

				if (_clients.Any(c => c.Name == client.Name))
					throw new Exception(_localizer[nameof(R.ClientWithThisNameAlreadyExists)]);

				_clients.Add(client);
			}

			client.SendingMessage += Client_SendingMessage;
		}

        public void DeleteClient(string name)
        {
			lock (_clientsSync)
			{
				foreach (var client in _clients)
				{
					if (client.Name == name)
					{
						_clients.Remove(client);
						client.SendingMessage -= Client_SendingMessage;
						client.Dispose();
						break;
					}
				}
			}
		}

		public Task DeleteClientAsync(string name)
		{
			return Task.Run(() =>
			{
				try
				{
					DeleteClient(name);
				}
				catch (Exception exc)
				{
					OnError(exc, false);
				}
			});
		}

		public void ReplaceInfo(string name, IAccountInfo computerAccount)
        {
            lock (_clientsSync)
            {
                foreach (var client in _clients)
                {
                    if (client.Name == name)
                    {
                        client.ReplaceInfo(computerAccount);
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Клиент отправляет сообщение
        /// </summary>
        /// <param name="obj">Отправляемое сообщение</param>
        private void Client_SendingMessage(IClient sender, Message m)
        {
            if (string.IsNullOrWhiteSpace(m.Receiver))
                return;

            if (m.Receiver[0] == AnonymousSenderPrefix)
            {
                IConnection connection;
                // Анонимное сообщение (серверу)
                lock (_connectionsSync)
                {
                    connection = Connections.FirstOrDefault(conn => conn.Id == m.Receiver.Substring(1));
                }

                if (connection != null)
                {
                    connection.SendMessage(new Message(m.Text, m.Sender, Constants.Everybody, m.IsSystem, m.IsPrivate));
                }
            }
            else
            {
                ProcessOutgoingMessage(m);
            }
        }

        /// <summary>
        /// Находится ли клиент с данным именем онлайн
        /// </summary>
        /// <param name="name">Имя клиента</param>
        /// <returns>Находится ли онлайн</returns>
        public bool IsOnline(string name)
        {
            return AllClients.Contains(name);
        }

		public bool IsOnlineInternal(string name)
		{
            foreach (var item in _clients)
			{
                if (item.Name == name)
                {
                    return true;
                }
			}

			lock (_connectionsSync)
			{
				foreach (var connection in Connections)
				{
					lock (connection.ClientsSync)
					{
                        foreach (var str in connection.Clients)
                        {
                            if (str == name)
                            {
                                return true;
                            }
                        }
					}
				}
			}

			return false;
		}

        /// <summary>
        /// Находится ли клиент с данным именем онлайн
        /// </summary>
        /// <param name="name">Имя клиента</param>
        /// <returns>"+", если оналйн; "-", в противном случае</returns>
        public string IsOnlineString(string name) => IsOnline(name) ? "+" : "-";

        /// <summary>
        /// Расширенный список клиентов (включая внешние сервера)
        /// </summary>
        public IEnumerable<string> AllClients
        {
            get
            {
				// yield return тут не годится из-за большого числа блокировок
				var result = new List<string>();
                lock (_clientsSync)
                {
                    foreach (var item in _clients)
                    {
						result.Add(item.Name);
                    }
                }

                lock (_connectionsSync)
                {
                    foreach (var s in Connections)
                    {
                        lock (s.ClientsSync)
                        {
                            foreach (var str in s.Clients)
                            {
                                result.Add(str);
                            }
                        }
                    }
                }

				return result;
            }
        }

        /// <summary>
        /// Закрытие сервера
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            IClient[] clientArray;
            var getLock = Monitor.TryEnter(_clientsSync, 5000);
            if (!getLock)
            {
                Trace.TraceError($"Cannot get {nameof(_clientsSync)} in Dispose()!");
            }

            try
            {
                clientArray = _clients.ToArray();
                _clients.Clear();
            }
            finally
            {
                if (getLock)
                {
                    Monitor.Exit(_clientsSync);
                }
            }

            foreach (var client in clientArray)
            {
                client.Dispose();
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}
