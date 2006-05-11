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
using System.Collections;
using DOL.Database.IDaos;
using DOL.Database.TransferObjects;
using NHibernate.Expression;

namespace DOL.Database.NHibernateDaos
{
	/// <summary>
	/// Bind point table
	/// </summary>
	class BindPointDao : IBindPointDao
	{
		/// <summary>
		/// save all bindpoint unactive
		/// </summary>
		public void SaveAll()
		{
			//TODO flush NH cache
		}

		/// <summary>
		/// get all the bindpoint of region in DB
		/// </summary>
		/// <param name="regionID">look for BP in this region</param>
		/// <returns> list of bind point</returns>
		public IList SelectByRegion(int regionID)
		{
			return NHDatabase.Instance.SelectObjects(typeof(DbBindPoint), Expression.Eq("Region", regionID));
		}
	}
}
