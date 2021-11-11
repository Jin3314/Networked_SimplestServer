using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using System.IO;
using UnityEngine.UI;

public class NetworkedServer : MonoBehaviour
{
    const int maxConnections = 1000;
    int reliableChannelID;
    int unreliableChannelID;
    int hostID;
    const int socketPort = 5491;

    LinkedList<PlayerAccount> playerAccounts;

    string playerAccountFilePath;

    int playerWaitingForMatch = -1;

    LinkedList<GameSession> gameSessions;

    // Start is called before the first frame update
    void Start()
    {

        playerAccountFilePath = Application.dataPath + Path.DirectorySeparatorChar + "PlayerAccountData.txt";

        NetworkTransport.Init();

        ConnectionConfig config = new ConnectionConfig();

        reliableChannelID = config.AddChannel(QosType.Reliable);
        unreliableChannelID = config.AddChannel(QosType.Unreliable);

        HostTopology topology = new HostTopology(config, maxConnections);

        hostID = NetworkTransport.AddHost(topology, socketPort, null);

        playerAccounts = new LinkedList<PlayerAccount>();

        gameSessions = new LinkedList<GameSession>();

        

        LoadPlayerAccounts();
    }

    // Update is called once per frame
    void Update()
    {

        int recHostID;
        int recConnectionID;
        int recChannelID;
        byte[] recBuffer = new byte[1024];
        int bufferSize = 1024;
        int dataSize;
        byte error = 0;


        NetworkEventType recNetworkEvent = NetworkTransport.Receive(out recHostID, out recConnectionID, out recChannelID, recBuffer, bufferSize, out dataSize, out error);

        switch (recNetworkEvent)
        {
            case NetworkEventType.Nothing:
                break;
            case NetworkEventType.ConnectEvent:
                Debug.Log("Connection, " + recConnectionID);
                break;
            case NetworkEventType.DataEvent:
                string msg = Encoding.Unicode.GetString(recBuffer, 0, dataSize);
                ProcessRecievedMsg(msg, recConnectionID);
                break;
            case NetworkEventType.DisconnectEvent:
                Debug.Log("Disconnection, " + recConnectionID);
                break;
        }


    }
  
    public void SendMessageToClient(string msg, int id)
    {
        byte error = 0;
        byte[] buffer = Encoding.Unicode.GetBytes(msg);
        NetworkTransport.Send(hostID, id, reliableChannelID, buffer, msg.Length * sizeof(char), out error);
    }
    
    private void ProcessRecievedMsg(string msg, int id)
    {
        Debug.Log("msg recieved = " + msg + ".  connection id = " + id);

        string[] csv = msg.Split(',');

        int signifier = int.Parse(csv[0]);




        if (signifier == ClientToServerSignifiers.CreateAccount)
        {
            string n = csv[1];
            string p = csv[2];

            bool isUnique = true;

            foreach(PlayerAccount pa in playerAccounts)
            {
                if(pa.name == n)
                {
                    isUnique = false;
                    break;
                }
            }

            if(isUnique)
            {
                playerAccounts.AddLast(new PlayerAccount(n, p));
                SendMessageToClient(ServerToClientSignifiers.LoginResponse + "," + LoginResponses.Success, id);

                SavePlayerAccounts();
            }
            else
            {
                SendMessageToClient(ServerToClientSignifiers.LoginResponse + "," + LoginResponses.FailureNameInUse, id);
            }
        }
        else if(signifier == ClientToServerSignifiers.Login)
        {
            string n = csv[1];
            string p = csv[2];

            bool hasBeenFound = false;
         

            foreach (PlayerAccount pa in playerAccounts)
            {
                if (pa.name == n)
                {
                    if(pa.password == p)
                    {
                        SendMessageToClient(ServerToClientSignifiers.LoginResponse + "," + LoginResponses.Success, id);
                    
                    }
                    else
                    {
                        SendMessageToClient(ServerToClientSignifiers.LoginResponse + "," + LoginResponses.FailureIncorrectPassword, id);
                    
                    }
                    hasBeenFound = true;
                    break;
                }
            }
            if(!hasBeenFound)
            {
                SendMessageToClient(ServerToClientSignifiers.LoginResponse + "," + LoginResponses.FailureNameNotFound, id);
            }

        }
        else if (signifier == ClientToServerSignifiers.AddToGameSessionQueue)
        {
            if (playerWaitingForMatch == -1)
            {
                playerWaitingForMatch = id;
            }
            else
            {
                GameSession gs = new GameSession(playerWaitingForMatch, id);
                gameSessions.AddLast(gs);

                int randomNumberForGameSymbol = Random.Range(0, 2);

                string playerWaitingForMatchSymbol = (randomNumberForGameSymbol == 0) ? "X" : "O";

                string currentPlayersSymbol = (playerWaitingForMatchSymbol == "X") ? "O" : "X";

                int playerWaitingForMatchMovesFirst = Random.Range(0, 2);

                int currentPlayersMove = (playerWaitingForMatchMovesFirst == 1) ? 0 : 1;

                SendMessageToClient(string.Join(",", ServerToClientSignifiers.GameSessionStarted.ToString(), playerWaitingForMatchSymbol, playerWaitingForMatchMovesFirst), playerWaitingForMatch);

                SendMessageToClient(string.Join(",", ServerToClientSignifiers.GameSessionStarted, currentPlayersSymbol, currentPlayersMove) + "", id);

                playerWaitingForMatch = -1;
            }

        }
        else if (signifier == ClientToServerSignifiers.TicTacToePlay)
        {
           // Debug.Log("our next action item beckons!");

           GameSession gs = FindGameSessionWithPlayerID(id);

            if(gs.playerID1 == id)
                SendMessageToClient(ServerToClientSignifiers.OpponentTicTacToePlay + "", gs.playerID2);
            else
                SendMessageToClient(ServerToClientSignifiers.OpponentTicTacToePlay + "", gs.playerID1);

        

        }
        else if (signifier == ClientToServerSignifiers.AnyMove)
        {
            GameSession gs = FindGameSessionWithPlayerID(id);

            if (gs.playerID1 == id)
            {
                SendMessageToClient(string.Join(",", ServerToClientSignifiers.OpponentChoice.ToString(), csv[1]), gs.playerID2);
            }
            else
            {
                SendMessageToClient(string.Join(",", ServerToClientSignifiers.OpponentChoice.ToString(), csv[1]), gs.playerID1);
            }
        }
    }

    private void SavePlayerAccounts()
    {
        StreamWriter sw = new StreamWriter(playerAccountFilePath);

        foreach (PlayerAccount pa in playerAccounts)
        {
            sw.WriteLine(pa.name + "," + pa.password);
        }
        sw.Close();
    }

    private void LoadPlayerAccounts()
    {
        if(File.Exists(playerAccountFilePath))
        {
            StreamReader sr = new StreamReader(playerAccountFilePath);

            string line;
            while ((line = sr.ReadLine()) != null)
            {
                string[] csv = line.Split(',');

                PlayerAccount pa = new PlayerAccount(csv[0], csv[1]);
                playerAccounts.AddLast(pa);
            }
        }

       

       
    }

    private GameSession FindGameSessionWithPlayerID(int id)
    {
        foreach(GameSession gs in gameSessions)
        {
            if (gs.playerID1 == id || gs.playerID2 == id)
                return gs;
        }
        return null;
    }

}


public class PlayerAccount
{
    public string name, password;

    public PlayerAccount(string Name, string Password)
    {
        name = Name;
        password = Password;
    }
}

public class GameSession
{
    public int playerID1, playerID2;

    public GameSession(int PlayerID1, int PlayerID2)
    {
        playerID1 = PlayerID1;
        playerID2 = PlayerID2;
    }
    //hold two clients
}


public static class ClientToServerSignifiers
{
    public const int Login = 1;

    public const int CreateAccount = 2;

    public const int AddToGameSessionQueue = 3;

    public const int TicTacToePlay = 4;

    public const int AnyMove = 5;
}


public static class ServerToClientSignifiers
{
    public const int LoginResponse = 1;

    public const int GameSessionStarted = 2;

    public const int OpponentTicTacToePlay = 3;

    public const int OpponentChoice = 4;
}

public static class LoginResponses
{
    public const int Success = 1;

    public const int FailureNameInUse = 2;

    public const int FailureNameNotFound = 3;

    public const int FailureIncorrectPassword = 4;
}