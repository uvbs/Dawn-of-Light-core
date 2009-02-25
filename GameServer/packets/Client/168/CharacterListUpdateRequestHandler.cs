/*
 * DAWN OF LIGHT - The first free open source DAoC server emulator
 *
 * This program is free software; you can redistribute it and/or
 * modify it under the terms of the GNU General Public License
 * as published by the Free Software Foundation; either version 2
 * of the License, or (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program; if not, write to the Free Software
 * Foundation, Inc., 59 Temple Place - Suite 330, Boston, MA  02111-1307, USA.
 *
 */
using System;
using System.Reflection;
using DOL.GS;
using DOL.Events;
using DOL.Database;
using DOL.GS.ServerProperties;
using log4net;
using System.Text.RegularExpressions;

namespace DOL.GS.PacketHandler.Client.v168
{
	[PacketHandlerAttribute(PacketHandlerType.TCP, 0x57 ^ 168, "Handles character creation requests")]
	public class CharacterListUpdateRequestHandler : IPacketHandler
	{
		/// <summary>
		/// Defines a logger for this class.
		/// </summary>
		private static readonly ILog log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

		public int HandlePacket(GameClient client, GSPacketIn packet)
		{
			//DOLConsole.WriteLine("Character creation!\n");
			string accountName = packet.ReadString(24);
			if (!accountName.StartsWith(client.Account.Name))// TODO more correctly check, client send accountName as account-S, -N, -H (if it not fit in 20, then only account)
			{
                DBBannedAccount b = new DBBannedAccount();
                b.Author = "SERVER";
                b.Ip = client.TcpEndpoint;
                b.Account = client.Account.Name;
                b.DateBan = DateTime.Now;
                b.Type = "B";
                b.Reason = String.Format("Autoban wrong Account '{0}'", GameServer.Database.Escape(accountName));
                GameServer.Database.AddNewObject(b);
                GameServer.Database.SaveObject(b);
                client.Disconnect(); return 0;
			}
			int charsCount = client.Version < GameClient.eClientVersion.Version173 ? 8 : 10;
			for (int i = 0; i < charsCount; i++)
			{
				string charname = packet.ReadString(24);
				if (charname.Length == 0)
				{
					//If the charname is empty, skip the other bytes
					packet.Skip(160);
				}
				else
				{
					// Graveen: changed the following to allow GMs to have special chars in their names (_,-, etc..)
					Regex nameCheck = new Regex ("^[A-Z][a-zA-Z]");
					if(charname.Length<3 || !nameCheck.IsMatch(charname))
					   	if (client.Account.PrivLevel==1)
						{
							DBBannedAccount b = new DBBannedAccount();
							b.Author = "SERVER";
							b.Ip = client.TcpEndpoint;
							b.Account = client.Account.Name;
							b.DateBan = DateTime.Now;
							b.Type = "B";
							b.Reason = String.Format("Autoban bad CharName '{0}'", GameServer.Database.Escape(charname));
							GameServer.Database.AddNewObject(b);
							GameServer.Database.SaveObject(b);
							client.Disconnect();
							return 1;
						}
     					
					String select = String.Format("Name = '{0}'", GameServer.Database.Escape(charname));
					Character character = (Character)GameServer.Database.SelectObject(typeof(Character), select);
					if (character != null)
					{
						if (character.AccountName != client.Account.Name)
						{
                            DBBannedAccount b = new DBBannedAccount();
                            b.Author = "SERVER";
                            b.Ip = client.TcpEndpoint;
                            b.Account = client.Account.Name;
                            b.DateBan = DateTime.Now;
                            b.Type = "B";
                            b.Reason = String.Format("Autoban CharName '{0}' on wrong Account '{1}'", GameServer.Database.Escape(charname), GameServer.Database.Escape(client.Account.Name));
                            GameServer.Database.AddNewObject(b);
                            GameServer.Database.SaveObject(b);
                            client.Disconnect(); return 1;
						}
						// update old character
                        byte customization = (byte)packet.ReadByte();
                        int newModel = character.CurrentModel;

                        if (customization == 1 || customization == 2 || customization == 3)
                        {
                            bool flagChangedStats = false;
                            character.EyeSize = (byte)packet.ReadByte();
                            character.LipSize = (byte)packet.ReadByte();
                            character.EyeColor = (byte)packet.ReadByte();
                            character.HairColor = (byte)packet.ReadByte();
                            character.FaceType = (byte)packet.ReadByte();
                            character.HairStyle = (byte)packet.ReadByte();
                            packet.Skip(3);
                            character.MoodType = (byte)packet.ReadByte();
                            packet.Skip(89); // Skip location string, race string, classe string, level ,class ,realm and startRaceGender
                            newModel = packet.ReadShortLowEndian(); //read new model
                            if (customization != 3 && client.Version >= GameClient.eClientVersion.Version189)
                            {
                                packet.Skip(6); // Region ID + character Internal ID
                                int[] stats = new int[8];
                                stats[0] = (byte)packet.ReadByte(); // Strength
                                stats[2] = (byte)packet.ReadByte(); // Dexterity
                                stats[1] = (byte)packet.ReadByte(); // Constitution
                                stats[3] = (byte)packet.ReadByte(); // Quickness
                                stats[4] = (byte)packet.ReadByte(); // Intelligence
                                stats[5] = (byte)packet.ReadByte(); // Piety
                                stats[6] = (byte)packet.ReadByte(); // Empathy
                                stats[7] = (byte)packet.ReadByte(); // Charisma
                                packet.Skip(43);// armor models/armor color/weapon models/active weapon slots/siZone
                                //stats[1] = (byte)packet.ReadByte(); // Constitution
                                byte newConstitution = (byte)packet.ReadByte();
                                if (newConstitution > 0)
                                    stats[1] = newConstitution;
                                flagChangedStats |= stats[0] != character.Strength;
                                flagChangedStats |= stats[1] != character.Constitution;
                                flagChangedStats |= stats[2] != character.Dexterity;
                                flagChangedStats |= stats[3] != character.Quickness;
                                flagChangedStats |= stats[4] != character.Intelligence;
                                flagChangedStats |= stats[5] != character.Piety;
                                flagChangedStats |= stats[6] != character.Empathy;
                                flagChangedStats |= stats[7] != character.Charisma;
                                if (flagChangedStats)
                                {
                                    IClassSpec cl = ScriptMgr.FindClassSpec(character.Class);

                                    if (cl != null)
                                    {
                                        int points = 0;
                                        int[] leveledStats = new int[8];
                                        int[] raceStats = new int[8];
                                        bool valid = true;
                                        for (int j = 0; j < 8; j++)
                                        {
                                            eStat stat = (eStat)CheckCharacter.eStatIndex[j];
                                            raceStats[j] = CheckCharacter.STARTING_STATS[character.Race][j];
                                            for (int level = character.Level; level > 5; level--)
                                            {
                                                if (cl.PrimaryStat != eStat.UNDEFINED && cl.PrimaryStat == stat)
                                                {
                                                    leveledStats[j]++;
                                                }
                                                if (cl.SecondaryStat != eStat.UNDEFINED && cl.SecondaryStat == stat)
                                                {
                                                    if ((level - 6) % 2 == 0)
                                                        leveledStats[j]++;
                                                }
                                                if (cl.TertiaryStat != eStat.UNDEFINED && cl.TertiaryStat == stat)
                                                {
                                                    if ((level - 6) % 3 == 0)
                                                        leveledStats[j]++;
                                                }
                                            }
                                            int result = stats[j] - leveledStats[j] - raceStats[j];
                                            bool validBeginStat = result >= 0;
                                            int point = result;
                                            string pref = "";
                                            if (cl.PrimaryStat != eStat.UNDEFINED && cl.PrimaryStat == stat)
                                                pref = "1)";
                                            if (cl.SecondaryStat != eStat.UNDEFINED && cl.SecondaryStat == stat)
                                                pref = "2)";
                                            if (cl.TertiaryStat != eStat.UNDEFINED && cl.TertiaryStat == stat)
                                                pref = "3)";
                                            point += Math.Max(0, result - 10); //two points used
                                            point += Math.Max(0, result - 15); //three points used
                                            log.Info(string.Format("{0,-2} {1,-3}:{2, 3} {3,3} {4,3} {5,3} {6,2} {7} {8}",
                                                pref, (stat == eStat.STR) ? "STR" : stat.ToString(), stats[j], leveledStats[j], stats[j] - leveledStats[j], raceStats[j], result, point, (validBeginStat) ? "" : "Not Valid"));
                                            points += point;
                                            if (!validBeginStat)
                                            {
                                                valid = false;
                                                if (client.Account.PrivLevel == 1)
                                                {
                                                    DBBannedAccount b = new DBBannedAccount();
                                                    b.Author = "SERVER";
                                                    b.Ip = client.TcpEndpoint;
                                                    b.Account = client.Account.Name;
                                                    b.DateBan = DateTime.Now;
                                                    b.Type = "B";
                                                    b.Reason = String.Format("Autoban Hack char update : Wrong {0} point:{1}", (stat == eStat.STR) ? "STR" : stat.ToString(), result);
                                                    GameServer.Database.AddNewObject(b);
                                                    GameServer.Database.SaveObject(b);
                                                    client.Disconnect();
                                                    return 1;
                                                }
                                            }
                                        }
                                        if (points != 30)
                                        {
                                            log.Warn("v189+ stat respec points used:" + points);
                                            valid = false;
                                            if (client.Account.PrivLevel == 1)
                                            {
                                                DBBannedAccount b = new DBBannedAccount();
                                                b.Author = "SERVER";
                                                b.Ip = client.TcpEndpoint;
                                                b.Account = client.Account.Name;
                                                b.DateBan = DateTime.Now;
                                                b.Type = "B";
                                                b.Reason = String.Format("Autoban Hack char update : Wrong total points used:{0}", points);
                                                GameServer.Database.AddNewObject(b);
                                                GameServer.Database.SaveObject(b);
                                                client.Disconnect();
                                                return 1;
                                            }
                                        }
                                        if (valid/* && client.Account.PrivLevel > 1*/)
                                        {
                                            character.Strength = (byte)stats[0];
                                            character.Constitution = (byte)stats[1];
                                            character.Dexterity = (byte)stats[2];
                                            character.Quickness = (byte)stats[3];
                                            character.Intelligence = (byte)stats[4];
                                            character.Piety = (byte)stats[5];
                                            character.Empathy = (byte)stats[6];
                                            character.Charisma = (byte)stats[7];
                                            //if (client.Player != null && character.Name == client.Player.Name && client.ActiveCharIndex != -1 && client.Account.Characters[client.ActiveCharIndex].Name == character.Name)
                                            Character[] chars = client.Account.Characters;
                                            for (int z = 0; z < chars.Length; z++)
                                            {
                                                if (chars[z].Name != character.Name) continue;
                                                //log.Error(string.Format("found activePlayer:[{0}] {1} {2}", client.ActiveCharIndex, client.Player.Name, character.Name));
                                                if (log.IsInfoEnabled)
                                                    log.Info(String.Format("Character {0} updated in cache!\n", charname));
                                                //client.Account.Characters = null;
                                                if (client.Player != null)
                                                {
                                                    client.Player.PlayerCharacter.Strength = (byte)stats[0];
                                                    client.Player.PlayerCharacter.Constitution = (byte)stats[1];
                                                    client.Player.PlayerCharacter.Dexterity = (byte)stats[2];
                                                    client.Player.PlayerCharacter.Quickness = (byte)stats[3];
                                                    client.Player.PlayerCharacter.Intelligence = (byte)stats[4];
                                                    client.Player.PlayerCharacter.Piety = (byte)stats[5];
                                                    client.Player.PlayerCharacter.Empathy = (byte)stats[6];
                                                    client.Player.PlayerCharacter.Charisma = (byte)stats[7];
                                                }
                                                client.Account.Characters[z].Strength = (byte)stats[0];
                                                client.Account.Characters[z].Constitution = (byte)stats[1];
                                                client.Account.Characters[z].Dexterity = (byte)stats[2];
                                                client.Account.Characters[z].Quickness = (byte)stats[3];
                                                client.Account.Characters[z].Intelligence = (byte)stats[4];
                                                client.Account.Characters[z].Piety = (byte)stats[5];
                                                client.Account.Characters[z].Empathy = (byte)stats[6];
                                                client.Account.Characters[z].Charisma = (byte)stats[7];
                                            }
                                        }
                                    }
                                    else
                                    {
                                        if (log.IsErrorEnabled)
                                            log.Error("No CharacterClass with ID " + character.Class + " found");
                                    }
                                }
                            }
                            else
                            {
                                packet.Skip(58); // skip all other things
                            }
                            if (customization == 2) // change player customization
                            {
                                if (client.Account.PrivLevel == 1 && ((newModel >> 11) & 3) == 0) // Player size must be > 0 (from 1 to 3)
                                {
                                    DBBannedAccount b = new DBBannedAccount();
                                    b.Author = "SERVER";
                                    b.Ip = client.TcpEndpoint;
                                    b.Account = client.Account.Name;
                                    b.DateBan = DateTime.Now;
                                    b.Type = "B";
                                    b.Reason = String.Format("Autoban Hack char update : zero character size in model:{0}", newModel);
                                    GameServer.Database.AddNewObject(b);
                                    GameServer.Database.SaveObject(b);
                                    client.Disconnect();
                                    return 1;
                                }
                                if ((ushort)newModel != character.CreationModel)
                                {
                                    //character.CreationModel = newModel;
                                    character.CurrentModel = newModel;
                                }
                                character.CustomisationStep = 2; // disable config button

                                GameServer.Database.SaveObject(character);

                                if (log.IsInfoEnabled)
                                    log.Info(String.Format("Character {0} face proprieties configured by account {1}!\n", charname, client.Account.Name));
                            }
                             //it's possible ?
                            else if (customization == 3) //auto config
                            {
                                character.CustomisationStep = 3; // enable config button to player

                                GameServer.Database.SaveObject(character);

                                if (log.IsInfoEnabled)
                                    log.Info(String.Format("Character {0} face proprieties auto updated!\n", charname));
                            }							
                            else if (customization == 1 && flagChangedStats) //changed stat only for 1.89+
                            {
                                GameServer.Database.SaveObject(character);

                                if (log.IsInfoEnabled)
                                    log.Info(String.Format("Character {0} stat updated!\n", charname));
                            }
                        }
					}
					else
					{
						// create new character

						Account account = client.Account;
						//TODO new db framework
						Character ch = new Character();
						ch.AccountName = account.Name;
						ch.Name = charname;

						if (packet.ReadByte() == 0x01)
						{
							ch.EyeSize = (byte)packet.ReadByte();
							ch.LipSize = (byte)packet.ReadByte();
							ch.EyeColor = (byte)packet.ReadByte();
							ch.HairColor = (byte)packet.ReadByte();
							ch.FaceType = (byte)packet.ReadByte();
							ch.HairStyle = (byte)packet.ReadByte();
							packet.Skip(3);
							ch.MoodType = (byte)packet.ReadByte();

							ch.CustomisationStep = 2; // disable config button

							packet.Skip(13);
						}
						else
						{
							packet.Skip(23);
						}

						packet.Skip(24); //Location String
						ch.LastName = "";
						ch.GuildID = "";
						packet.Skip(24); //Skip class name
						packet.Skip(24); //Skip race name
						ch.Level = packet.ReadByte(); //not safe!
						ch.Level = 1;
						ch.Class = packet.ReadByte();
						ch.Realm = packet.ReadByte();
						if (client.Version >= GameClient.eClientVersion.Version193)
						{
							if (log.IsWarnEnabled)
								log.Warn("Creation 1.93+ character, class:" + ch.Class + ", realm:" + ch.Realm);
							if (ch.Class >= CheckCharacter.ADVANCED_CLASS_TO_BASE_CLASS.Length)
							{
								log.Error(client.Account.Name + " tried to create a character with wrong class ID: " + ch.Class + ", realm:" + ch.Realm);
								if (ServerProperties.Properties.BAN_HACKERS)
								{
									DBBannedAccount b = new DBBannedAccount();
									b.Author = "SERVER";
									b.Ip = client.TcpEndpoint;
									b.Account = client.Account.Name;
									b.DateBan = DateTime.Now;
									b.Type = "B";
									b.Reason = string.Format("Autoban character create class: id:{0} realm:{1} name:{2} account:{3}", ch.Class, ch.Realm, ch.Name, account.Name);
									GameServer.Database.AddNewObject(b);
									GameServer.Database.SaveObject(b);
									client.Disconnect();
								}
								//								client.Out.SendCharacterOverview((eRealm)ch.Realm);
								return 1;
							}
							else
							{
								ch.Class = CheckCharacter.ADVANCED_CLASS_TO_BASE_CLASS[ch.Class];
								if (log.IsWarnEnabled)
									log.Warn("Change to base class:" + ch.Class + ", realm:" + ch.Realm);
							}
						}
						ch.AccountSlot = i + ch.Realm * 100;

						//The following byte contains
						//1bit=start location ... in SI you can choose ...
						//1bit=first race bit
						//1bit=unknown
						//1bit=gender (0=male, 1=female)
						//4bit=race
						byte startRaceGender = (byte)packet.ReadByte();

						ch.Race = (startRaceGender & 0x0F) + ((startRaceGender & 0x40) >> 2);
						//only players(plvl 1) are disabled. GMs and Admins can still be Minotaurs.
						if (Properties.DISABLE_MINOTAURS && client.Account.PrivLevel == 1)
						{
							switch ((eRace)ch.Race)
							{
								case eRace.AlbionMinotaur:
								case eRace.HiberniaMinotaur:
								case eRace.MidgardMinotaur:
									{
										log.Error(client.Account.Name + " tried to create a minotaur, creation of them is disabled");
										client.Out.SendCharacterOverview((eRealm)ch.Realm);
										return 1;
									}
							}
						}
						ch.Gender = ((startRaceGender >> 4) & 0x01);
						//DOLConsole.WriteLine("startRaceGender="+startRaceGender+"; Race="+ch.Race+"; Gender="+ch.Gender);

						bool siStartLocation = ((startRaceGender >> 7) != 0);

						ch.CreationModel = packet.ReadShortLowEndian();
						ch.CurrentModel = ch.CreationModel;
						ch.Region = packet.ReadByte();
						packet.Skip(1); //TODO second byte of region unused currently
						packet.Skip(4); //TODO Unknown Int / last used?
						//packet.Skip(8); //TODO stats
						ch.Strength = (byte)packet.ReadByte();
						ch.Dexterity = (byte)packet.ReadByte();
						ch.Constitution = (byte)packet.ReadByte();
						ch.Quickness = (byte)packet.ReadByte();
						ch.Intelligence = (byte)packet.ReadByte();
						ch.Piety = (byte)packet.ReadByte();
						ch.Empathy = (byte)packet.ReadByte();
						ch.Charisma = (byte)packet.ReadByte();
						packet.Skip(44); //TODO equipment

						// check if client tried to create invalid char
						if (!CheckCharacter.IsCharacterValid(ch))
						{
							if (log.IsWarnEnabled)
								log.Warn(ch.AccountName + " tried to create invalid character:" +
								"\nchar name=" + ch.Name + ", race=" + ch.Race + ", realm=" + ch.Realm + ", class=" + ch.Class + ", region=" + ch.Region +
								"\nstr=" + ch.Strength + ", con=" + ch.Constitution + ", dex=" + ch.Dexterity + ", qui=" + ch.Quickness + ", int=" + ch.Intelligence + ", pie=" + ch.Piety + ", emp=" + ch.Empathy + ", chr=" + ch.Charisma);

							if (client.Account.Realm == 0) client.Out.SendRealm(eRealm.None);
							else client.Out.SendCharacterOverview((eRealm)client.Account.Realm);
							return 1;
						}

						ch.CreationDate = DateTime.Now;

						ch.Endurance = 100;
						ch.MaxEndurance = 100;
						ch.Concentration = 100;
						ch.MaxSpeed = GamePlayer.PLAYER_BASE_SPEED;

						//if the server property for disable tutorial is set, we load in the classic starting locations
						if (ch.Region == 27 && ServerProperties.Properties.DISABLE_TUTORIAL)
						{
							switch (ch.Realm)
							{
								case 1: ch.Region = 1; break;
								case 2: ch.Region = 100; break;
								case 3: ch.Region = 200; break;
							}
						}



						ch.Xpos = 505603;
						ch.Ypos = 494709;
						ch.Zpos = 2463;
						ch.Direction = 5947;

						if (ch.Region == 51 && ch.Realm == 1)//Albion SI start point (I hope)
						{
							ch.Xpos = 526252;
							ch.Ypos = 542415;
							ch.Zpos = 3165;
							ch.Direction = 5286;
						}
						if (ch.Region != 51 && ch.Realm == 1)//Albion start point (Church outside Camelot/humberton)
						{
							ch.Xpos = 505603;
							ch.Ypos = 494709;
							ch.Zpos = 2463;
							ch.Direction = 5947;
							//ch.Region = 1;
							//DOLConsole.WriteLine(String.Format("Character ClassName:"+ch.ClassName+" created!"));
							//DOLConsole.WriteLine(String.Format("Character RaceName:"+ch.RaceName+" created!"));
						}
						if (ch.Region == 151 && ch.Realm == 2)//Midgard SI start point
						{
							ch.Xpos = 293720;
							ch.Ypos = 356408;
							ch.Zpos = 3488;
							ch.Direction = 6670;
						}
						if (ch.Region != 151 && ch.Realm == 2)//Midgard start point (Fort Atla)
						{
							ch.Xpos = 749103;
							ch.Ypos = 815835;
							ch.Zpos = 4408;
							ch.Direction = 7915;
							//ch.Region = 100;
							//DOLConsole.WriteLine(String.Format("Character ClassName:"+ch.ClassName+" created!"));
							//DOLConsole.WriteLine(String.Format("Character RaceName:"+ch.RaceName+" created!"));
						}
						if (ch.Region == 181 && ch.Realm == 3)//Hibernia SI start point
						{
							ch.Xpos = 426483;
							ch.Ypos = 440626;
							ch.Zpos = 5952;
							ch.Direction = 2403;
						}
						if (ch.Region != 181 && ch.Realm == 3)//Hibernia start point (Mag Mel)
						{
							ch.Xpos = 345900;
							ch.Ypos = 490867;
							ch.Zpos = 5200;
							ch.Direction = 4826;
							//ch.Region = 200;
							//DOLConsole.WriteLine(String.Format("Character ClassName:"+ch.ClassName+" created!"));
							//DOLConsole.WriteLine(String.Format("Character RaceName:"+ch.RaceName+" created!"));
						}

						// chars are bound on creation
						ch.BindRegion = ch.Region;
						ch.BindHeading = ch.Direction;
						ch.BindXpos = ch.Xpos;
						ch.BindYpos = ch.Ypos;
						ch.BindZpos = ch.Zpos;

						if (account.PrivLevel == 1 && Properties.STARTING_GUILD)
						{
							switch (ch.Realm)
							{
								case 1:
									switch (ServerProperties.Properties.SERV_LANGUAGE)
										{
											case "EN":
												ch.GuildID = GuildMgr.GuildNameToGuildID("Clan Cotswold");
												break; 
											case "DE":
												ch.GuildID = GuildMgr.GuildNameToGuildID("Klan Cotswold");
												break; 
											default:
												ch.GuildID = GuildMgr.GuildNameToGuildID("Clan Cotswold");
												break; 
										}													
									break;
								case 2:
									switch (ServerProperties.Properties.SERV_LANGUAGE)
										{
											case "EN":
												ch.GuildID = GuildMgr.GuildNameToGuildID("Mularn Protectors");
												break; 
											case "DE":
												ch.GuildID = GuildMgr.GuildNameToGuildID("Beschützer von Mularn");
												break; 
											default:
												ch.GuildID = GuildMgr.GuildNameToGuildID("Mularn Protectors");
												break; 
										}													
									break;
								case 3:
									switch (ServerProperties.Properties.SERV_LANGUAGE)
										{
											case "EN":
												ch.GuildID = GuildMgr.GuildNameToGuildID("Tir na Nog Adventurers");
												break; 
											case "DE":
												ch.GuildID = GuildMgr.GuildNameToGuildID("Tir na Nog-Abenteurer");
												break; 
											default:
												ch.GuildID = GuildMgr.GuildNameToGuildID("Tir na Nog Adventurers");
												break; 
										}
									break;
								default: break;
							}

							if (ch.GuildID != "")
								ch.GuildRank = 8;
						}

						if (Properties.STARTING_LEVEL > 1)
						{
							ch.Experience = GameServer.ServerRules.GetExperienceForLevel(Properties.STARTING_LEVEL);
						}

						if (Properties.STARTING_MONEY > 0)
						{
							long value = Properties.STARTING_MONEY;
							ch.Copper = Money.GetCopper(value);
							ch.Silver = Money.GetSilver(value);
							ch.Gold = Money.GetGold(value);
							ch.Platinum = Money.GetPlatinum(value);
						}

						if (Properties.STARTING_REALM_LEVEL > 0)
						{
							int realmLevel = Properties.STARTING_REALM_LEVEL;
							long rpamount = 0;
							if (realmLevel < GamePlayer.REALMPOINTS_FOR_LEVEL.Length)
								rpamount = GamePlayer.REALMPOINTS_FOR_LEVEL[realmLevel];

							// thanks to Linulo from http://daoc.foren.4players.de/viewtopic.php?t=40839&postdays=0&postorder=asc&start=0
							if (rpamount == 0)
								rpamount = (long)(25.0 / 3.0 * (realmLevel * realmLevel * realmLevel) - 25.0 / 2.0 * (realmLevel * realmLevel) + 25.0 / 6.0 * realmLevel);

							ch.RealmPoints = rpamount;
							ch.RealmLevel = realmLevel;
							ch.RealmSpecialtyPoints = realmLevel;
						}
						
						ch.RespecAmountRealmSkill += 2;

						setBasicCraftingForNewCharacter(ch);

						//Save the character in the database
						GameServer.Database.AddNewObject(ch);
						//Fire the character creation event
						GameEventMgr.Notify(DatabaseEvent.CharacterCreated, null, new CharacterEventArgs(ch, client));
						//add equipment
						StartupEquipment.AddEquipment(ch);
						//write changes
						GameServer.Database.SaveObject(ch);

						client.Account.Characters = null;

						if (log.IsInfoEnabled)
							log.Info(String.Format("Character {0} created!", charname));


						// 2008-01-29 Kakuri - Obsolete
						//GameServer.Database.WriteDatabaseTable( typeof( Character ) );
						GameServer.Database.FillObjectRelations(client.Account);
						client.Out.SendCharacterOverview((eRealm)ch.Realm);
					}
				}
			}
			return 1;
		}

		private void setBasicCraftingForNewCharacter(Character ch)
		{
			string serializedAllCraftingSkills = "";
			foreach (int craftingSkillId in Enum.GetValues(typeof(eCraftingSkill)))
			{
				if (craftingSkillId > 0)
				{
					serializedAllCraftingSkills += (int)craftingSkillId + "|1;";
					if (craftingSkillId == (int)eCraftingSkill._Last)
					{
						break;
					}
				}
			}
			if (serializedAllCraftingSkills.Length > 0)
			{
				serializedAllCraftingSkills = serializedAllCraftingSkills.Remove(serializedAllCraftingSkills.Length - 1);
			}
			ch.SerializedCraftingSkills = serializedAllCraftingSkills;
			ch.CraftingPrimarySkill = (int)eCraftingSkill.BasicCrafting;
		}

		#region CheckCharacter

		/// <summary>
		/// Provides methods to handle char creation checks
		/// </summary>
		protected class CheckCharacter
		{
			protected const int STR = 0;
			protected const int CON = 1;
			protected const int DEX = 2;
			protected const int QUI = 3;
			protected const int INT = 4;
			protected const int PIE = 5;
			protected const int EMP = 6;
			protected const int CHA = 7;

			/// <summary>
			/// Verify whether created character is valid
			/// </summary>
			/// <param name="ch">The character to check</param>
			/// <returns>True if valid</returns>
			public static bool IsCharacterValid(Character ch)
			{
				bool valid = true;
				try
				{
					if (ch.Realm > 3)
					{
						if (log.IsWarnEnabled)
							log.Warn("Wrong realm: " + ch.Realm);
						valid = false;
					}
					if (ch.Level != 1)
					{
						if (log.IsWarnEnabled)
							log.Warn("Wrong level: " + ch.Level);
						valid = false;
					}
					if (Array.IndexOf(STARTING_CLASSES[ch.Realm], ch.Class) == -1)
					{
						if (log.IsWarnEnabled)
							log.Warn("Wrong class: " + ch.Class + ", realm:" + ch.Realm);
						valid = false;
					}
					if (Array.IndexOf(RACES_CLASSES[ch.Race], ch.Class) == -1)
					{
						if (log.IsWarnEnabled)
							log.Warn("Wrong race: " + ch.Race + ", class:" + ch.Class);
						valid = false;
					}
					int pointsUsed = 0;
					pointsUsed += PointsUsed(ch.Race, STR, ch.Strength);
					pointsUsed += PointsUsed(ch.Race, CON, ch.Constitution);
					pointsUsed += PointsUsed(ch.Race, DEX, ch.Dexterity);
					pointsUsed += PointsUsed(ch.Race, QUI, ch.Quickness);
					pointsUsed += PointsUsed(ch.Race, INT, ch.Intelligence);
					pointsUsed += PointsUsed(ch.Race, PIE, ch.Piety);
					pointsUsed += PointsUsed(ch.Race, EMP, ch.Empathy);
					pointsUsed += PointsUsed(ch.Race, CHA, ch.Charisma);
					if (pointsUsed != 30)
					{
						if (log.IsWarnEnabled)
							log.Warn("Points used: " + pointsUsed);
						valid = false;
					}
				}
				catch (Exception e)
				{
					if (log.IsErrorEnabled)
						log.Error("CharacterCreation", e);
					return false;
				}
				return valid;
			}

			/// <summary>
			/// Calculates amount of starting points spent on one stat
			/// </summary>
			/// <param name="race">race index in starting stats array</param>
			/// <param name="statIndex">index of that stat in starting stats array</param>
			/// <param name="statValue">base+spent points in stat</param>
			/// <returns></returns>
			protected static int PointsUsed(int race, int statIndex, int statValue)
			{
				statValue -= STARTING_STATS[race][statIndex];
				int result = statValue; //one point used
				result += Math.Max(0, statValue - 10); //two points used
				result += Math.Max(0, statValue - 15); //three points used
				return result;
			}

            public static readonly byte[] eStatIndex = new byte[8]
			{
				(byte)eProperty.Strength,
				(byte)eProperty.Constitution,
				(byte)eProperty.Dexterity,
				(byte)eProperty.Quickness,
				(byte)eProperty.Intelligence,
				(byte)eProperty.Piety,
				(byte)eProperty.Empathy,
				(byte)eProperty.Charisma
			};

			/// <summary>
			/// All possible player races
			/// </summary>
			public static readonly int[][] STARTING_STATS = new int[][]
			{
				null, // "Unknown",
				//           STR CON DEX QUI INT PIE EMP CHA
				new int[8] { 60, 60, 60, 60, 60, 60, 60, 60 }, // Briton
				new int[8] { 45, 45, 60, 70, 80, 60, 60, 60 }, // Avalonian
				new int[8] { 70, 70, 50, 50, 60, 60, 60, 60 }, // Highlander
				new int[8] { 50, 50, 80, 60, 60, 60, 60, 60 }, // Saracen
				new int[8] { 70, 70, 50, 50, 60, 60, 60, 60 }, // Norseman
				new int[8] { 100, 70, 35, 35, 60, 60, 60, 60 }, // Troll
				new int[8] { 60, 80, 50, 50, 60, 60, 60, 60 }, // Dwarf
				new int[8] { 50, 50, 70, 70, 60, 60, 60, 60 }, // Kobold
				new int[8] { 60, 60, 60, 60, 60, 60, 60, 60 }, // Celt
				new int[8] { 90, 60, 40, 40, 60, 60, 70, 60 }, // Firbolg
				new int[8] { 40, 40, 75, 75, 70, 60, 60, 60 }, // Elf
				new int[8] { 40, 40, 80, 80, 60, 60, 60, 60 }, // Lurikeen
				new int[8] { 50, 60, 70, 50, 70, 60, 60, 60 }, // Inconnu
				new int[8] { 55, 45, 65, 75, 60, 60, 60, 60 }, // Valkyn
				new int[8] { 70, 60, 55, 45, 70, 60, 60, 60 }, // Sylvan
				new int[8] { 90, 70, 40, 40, 60, 60, 60, 60 }, // Half Ogre
				new int[8] { 55, 55, 55, 60, 60, 75, 60, 60 }, // Frostalf
				new int[8] { 60, 80, 50, 50, 60, 60, 60, 60 }, // Shar
				new int[8] { 80, 70, 50, 40, 60, 60, 60, 60 }, // AlbionMenotaur
				new int[8] { 80, 70, 50, 40, 60, 60, 60, 60 }, // MidgardMenotaur
				new int[8] { 80, 70, 50, 40, 60, 60, 60, 60 }, // HiberniaMenotaur
			};

			/// <summary>
			/// All possible player starting classes
			/// </summary>
			protected static readonly int[][] STARTING_CLASSES = new int[][]
			{
				null, // "Unknown",
				new int[] { (int)eCharacterClass.Fighter,
							(int)eCharacterClass.Acolyte,
							(int)eCharacterClass.Mage,
							(int)eCharacterClass.Elementalist,
							(int)eCharacterClass.AlbionRogue,
							(int)eCharacterClass.Disciple }, // Albion
				new int[] { (int)eCharacterClass.Viking,
							(int)eCharacterClass.Mystic,
							(int)eCharacterClass.Seer,
							(int)eCharacterClass.MidgardRogue }, // Midgard
				new int[] { (int)eCharacterClass.Guardian,
							(int)eCharacterClass.Stalker,
							(int)eCharacterClass.Naturalist,
							(int)eCharacterClass.Magician,
							(int)eCharacterClass.Forester }, // Hibernia
			};

			protected static readonly int[][] RACES_CLASSES = new int[][]
			{
				null, // "Unknown",
				//
				new int[] { (int)eCharacterClass.Fighter,
							(int)eCharacterClass.Acolyte,
							(int)eCharacterClass.Mage,
							(int)eCharacterClass.Elementalist,
							(int)eCharacterClass.AlbionRogue,
							(int)eCharacterClass.Disciple }, // Briton
				new int[] { (int)eCharacterClass.Fighter,
							(int)eCharacterClass.Acolyte,
							(int)eCharacterClass.Mage,
							(int)eCharacterClass.Elementalist }, // Avalonian
				new int[] { (int)eCharacterClass.Fighter,
							(int)eCharacterClass.Acolyte,
							(int)eCharacterClass.AlbionRogue }, // Highlander
				new int[] { (int)eCharacterClass.Fighter,
							(int)eCharacterClass.Mage,
							(int)eCharacterClass.AlbionRogue,
							(int)eCharacterClass.Disciple }, // Saracen
				new int[] { (int)eCharacterClass.Viking,
							(int)eCharacterClass.Mystic,
							(int)eCharacterClass.Seer,
							(int)eCharacterClass.MidgardRogue }, // Norseman
				new int[] { (int)eCharacterClass.Viking,
							(int)eCharacterClass.Mystic,
							(int)eCharacterClass.Seer }, // Troll
				new int[] { (int)eCharacterClass.Viking,
							(int)eCharacterClass.Mystic,
							(int)eCharacterClass.Seer,
							(int)eCharacterClass.MidgardRogue }, // Dwarf
				new int[] { (int)eCharacterClass.Viking,
							(int)eCharacterClass.Mystic,
							(int)eCharacterClass.Seer,
							(int)eCharacterClass.MidgardRogue }, // Kobold
				new int[] { (int)eCharacterClass.Guardian,
							(int)eCharacterClass.Stalker,
							(int)eCharacterClass.Naturalist,
							(int)eCharacterClass.Magician,
							(int)eCharacterClass.Forester }, // Celt
				new int[] { (int)eCharacterClass.Guardian,
							(int)eCharacterClass.Naturalist,
							(int)eCharacterClass.Forester }, // Firbolg
				new int[] { (int)eCharacterClass.Guardian,
							(int)eCharacterClass.Stalker,
							(int)eCharacterClass.Magician }, // Elf
				new int[] { (int)eCharacterClass.Guardian,
							(int)eCharacterClass.Stalker,
							(int)eCharacterClass.Magician }, // Lurikeen
				new int[] { (int)eCharacterClass.Fighter,
							(int)eCharacterClass.Acolyte,
							(int)eCharacterClass.Mage,
							(int)eCharacterClass.AlbionRogue,
							(int)eCharacterClass.Disciple }, // Inconnu
				new int[] { (int)eCharacterClass.Viking,
							(int)eCharacterClass.Mystic,
							(int)eCharacterClass.MidgardRogue }, // Valkyn
				new int[] { (int)eCharacterClass.Guardian,
							(int)eCharacterClass.Naturalist,
							(int)eCharacterClass.Forester }, // Sylvan
				new int[] { (int)eCharacterClass.Fighter,
							(int)eCharacterClass.Mage,
							(int)eCharacterClass.Elementalist }, // Half Ogre
				new int[] { (int)eCharacterClass.Viking,
							(int)eCharacterClass.Mystic,
							(int)eCharacterClass.Seer,
							(int)eCharacterClass.MidgardRogue }, // Frostalf
				new int[] { (int)eCharacterClass.Guardian,
							(int)eCharacterClass.Stalker,
							(int)eCharacterClass.Magician }, // Shar
				new int[] { (int)eCharacterClass.Fighter,
							(int)eCharacterClass.Acolyte },//AlbionMenotaur
				new int[] {	(int)eCharacterClass.Viking,
							(int)eCharacterClass.Seer }, //MidgardMenotaur
			    new int[] { (int)eCharacterClass.Guardian,
							(int)eCharacterClass.Naturalist }, //HiberniaMenotaur
			};

			public static readonly int[] ADVANCED_CLASS_TO_BASE_CLASS = new int[]
			{
				0, // "Unknown",
				(int)eCharacterClass.Fighter, 		// Paladin = 1,
				(int)eCharacterClass.Fighter, 		// Armsman = 2,
				(int)eCharacterClass.AlbionRogue, 	// Scout = 3,
				(int)eCharacterClass.AlbionRogue, 	// Minstrel = 4,
				(int)eCharacterClass.Elementalist, 	// Theurgist = 5,
				(int)eCharacterClass.Acolyte, 		// Cleric = 6,
				(int)eCharacterClass.Elementalist, 	// Wizard = 7,
				(int)eCharacterClass.Mage, 			// Sorcerer = 8,
				(int)eCharacterClass.AlbionRogue, 	// Infiltrator = 9,
				(int)eCharacterClass.Acolyte, 		// Friar = 10,
				(int)eCharacterClass.Fighter, 		// Mercenary = 11,
				(int)eCharacterClass.Disciple, 		// Necromancer = 12,
				(int)eCharacterClass.Mage, 			// Cabalist = 13,
				(int)eCharacterClass.Fighter, 		// Fighter = 14,
				(int)eCharacterClass.Elementalist, 	// Elementalist = 15,
				(int)eCharacterClass.Acolyte, 		// Acolyte = 16,
				(int)eCharacterClass.AlbionRogue, 	// AlbionRogue = 17,
				(int)eCharacterClass.Mage, 			// Mage = 18,
				(int)eCharacterClass.Fighter, 		// Reaver = 19,
				(int)eCharacterClass.Disciple, 		// Disciple = 20,
				(int)eCharacterClass.Viking, 		// Thane = 21,
				(int)eCharacterClass.Viking, 		// Warrior = 22,
				(int)eCharacterClass.MidgardRogue, 	// Shadowblade = 23,
				(int)eCharacterClass.Viking, 		// Skald = 24,
				(int)eCharacterClass.MidgardRogue, 	// Hunter = 25,
				(int)eCharacterClass.Seer, 			// Healer = 26,
				(int)eCharacterClass.Mystic, 		// Spiritmaster = 27,
				(int)eCharacterClass.Seer, 			// Shaman = 28,
				(int)eCharacterClass.Mystic, 		// Runemaster = 29,
				(int)eCharacterClass.Mystic, 		// Bonedancer = 30,
				(int)eCharacterClass.Viking, 		// Berserker = 31,
				(int)eCharacterClass.Viking, 		// Savage = 32,
				(int)eCharacterClass.Acolyte, 		// Heretic = 33,
				(int)eCharacterClass.Viking, 		// Valkyrie = 34,
				(int)eCharacterClass.Viking, 		// Viking = 35,
				(int)eCharacterClass.Mystic, 		// Mystic = 36,
				(int)eCharacterClass.Seer, 			// Seer = 37,
				(int)eCharacterClass.MidgardRogue, 	// MidgardRogue = 38,
				(int)eCharacterClass.Magician, 		// Bainshee = 39,
				(int)eCharacterClass.Magician, 		// Eldritch = 40,
				(int)eCharacterClass.Magician, 		// Enchanter = 41,
				(int)eCharacterClass.Magician, 		// Mentalist = 42,
				(int)eCharacterClass.Guardian, 		// Blademaster = 43,
				(int)eCharacterClass.Guardian, 		// Hero = 44,
				(int)eCharacterClass.Guardian, 		// Champion = 45,
				(int)eCharacterClass.Naturalist, 	// Warden = 46,
				(int)eCharacterClass.Naturalist, 	// Druid = 47,
				(int)eCharacterClass.Naturalist, 	// Bard = 48,
				(int)eCharacterClass.Stalker, 		// Nightshade = 49,
				(int)eCharacterClass.Stalker, 		// Ranger = 50,
				(int)eCharacterClass.Magician, 		// Magician = 51,
				(int)eCharacterClass.Guardian, 		// Guardian = 52,
				(int)eCharacterClass.Naturalist, 	// Naturalist = 53,
				(int)eCharacterClass.Stalker, 		// Stalker = 54,
				(int)eCharacterClass.Forester, 		// Animist = 55,
				(int)eCharacterClass.Forester, 		// Valewalker = 56,
				(int)eCharacterClass.Forester, 		// Forester = 57,
				(int)eCharacterClass.Stalker, 		// Vampiir = 58,
				(int)eCharacterClass.Mystic, 		// Warlock = 59,
				(int)eCharacterClass.Fighter, 		// Mauler_Alb = 60,
				(int)eCharacterClass.Viking, 		// Mauler_Mid = 61,
				(int)eCharacterClass.Guardian, 		// Mauler_Hib = 62,
			};
		}

		#endregion

	}
}
