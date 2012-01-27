using System;
using System.Threading;

namespace BubblesServer
{
    /// <summary>
    /// Repr�sente une condition utilisant un d�l�gu� pour v�rifier si elle est v�rifi�e ou non.
    /// </summary>
    public class PredicateCondition
    {
        #region Champs
        readonly ConditionPredicate predicate;
        readonly object conditionLock;
        #endregion
        #region Constructeur
        /// <summary>
        /// Cr�e une nouvelle condition avec le d�l�gu� sp�cifi�, qui utilise un objet interne pour la synchronisation.
        /// </summary>
        /// <param name="predicate"> D�l�gu� � utiliser pour v�rifier la condition. </param>        
        /// <remarks>
        /// Le verrou sur l'objet de synchronisation interne est acquis pendant l'appel du d�l�gu�.
        /// </remarks>
        public PredicateCondition( ConditionPredicate predicate ) : this( predicate, new object() )
        {
        }
        /// <summary>
        /// Cr�e une nouvelle condition avec le d�l�gu� sp�cifi� et l'objet de synchronisation sp�cifi�s.
        /// </summary>
        /// <param name="predicate"> D�l�gu� � utiliser pour v�rifier la condition. </param>
        /// <param name="conditionLock"> Objet � utiliser pour synchroniser l'acc�s � la condition. </param>
        /// <remarks>
        /// Le verrou sur <paramref name="conditionLock"/> est acquis pendant l'appel du d�l�gu�.
        /// </remarks>
        public PredicateCondition( ConditionPredicate predicate, object conditionLock )
        {
            if( predicate == null )
            {
                throw new ArgumentNullException( "predicate" );
            }
            else if( conditionLock == null )
            {
                throw new ArgumentNullException( "conditionLock" );
            }
            this.predicate = predicate;
            this.conditionLock = conditionLock;
        }
        #endregion
        #region Impl�mentation
        /// <summary>
        /// Attend que la condition soit v�rifi�e.
        /// </summary>
        public void Wait()
        {
            lock( conditionLock )
            {
                while( !predicate() )
                {
                    Monitor.Wait( conditionLock );
                }
            }
        }
        /// <summary>
        /// Signale que la valeur de la condition peut avoir chang�, si c'est le cas, r�veille un seul thread attendant que la condition soit v�rifi�e.
        /// </summary>
        public void Signal()
        {
            lock( conditionLock )
            {
                if( predicate() )
                {
                    Monitor.Pulse( conditionLock );
                }
            }
        }
        /// <summary>
        /// Signale que la valeur de la condition peut avoir chang�, si c'est le cas, r�veille tous les threads attendant que la condition soit v�rifi�e.
        /// </summary>
        public void SignalAll()
        {
            lock( conditionLock )
            {
                if( predicate() )
                {
                    Monitor.PulseAll( conditionLock );
                }
            }
        }
        #endregion
    }

    /// <summary>
    /// D�l�gu� indiquant si une condition est v�rifi�e ou non.
    /// </summary>
    /// <returns> true si la condition est actuellement v�rifi�e, false sinon. </returns>
    public delegate bool ConditionPredicate();
}
