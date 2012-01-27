using System;
using System.Threading;

namespace BubblesServer
{
    /// <summary>
    /// Buffer circulaire utilis� pour synchroniser des donn�es entre un producteur et un consommateur.
    /// Le producteur et le consommateur ne peuvent pas utiliser le buffer en m�me temps.
    /// </summary>
    public sealed class ExclusiveCircularQueue<T> : CircularQueue<T>
    {
		#region Champs
        //T[] buffer        :   synchronis� par syncExcl
        //int capacity      :   synchronis� par syncExcl
        //int available     :   synchronis� par syncExcl
        //int readPosition  :   synchronis� par syncExcl
        //int writePosition :   synchronis� par syncExcl
		readonly object syncExcl = new object();
        PredicateCondition canRead;
        PredicateCondition canWrite;
		#endregion
        #region Propri�t�s
        /// <summary>
        /// Obtient le nombre d'�l�ments dans le buffer.
        /// </summary>
        public override int Count
        {
            get
            {
                lock( syncExcl )
                {
                    return available;
                }
            }
        }
        /// <summary>
        /// Indique si aucun �l�ment ne peut �tre �crit dans le buffer.
        /// </summary>
        public override bool Full
        {
            get
            {
                lock( syncExcl )
                {
                    return (capacity == 0);
                }
            }
        }
        /// <summary>
        /// Indique si aucun �l�ment ne peut �tre lu dans le buffer.
        /// </summary>
        public override bool Empty
        {
            get
            {
                lock( syncExcl )
                {
                    return (available == 0);
                }
            }
        }
        #endregion
		#region Constructeur
        /// <summary>
        /// Cr�e un nouveau buffer circulaire avec la taille sp�cifi�e.
        /// </summary>
        /// <param name="size"> Taille du buffer. </param>
        public ExclusiveCircularQueue( int size ) : base( size )
		{
            Initialize();
        }
        /// <summary>
        /// Cr�e un nouveau buffer circulaire � partir des �l�ments pr�sents dans un tableau.
        /// </summary>
        /// <param name="array"> Tableau � partir duquel cr�er le buffer circulaire. </param>
        /// <param name="ownsArray"> Indique si le buffer utilise directement le tableau (true) ou copie son contenu dans un nouveau tableau (false). </param>
        /// <param name="empty"> Indique si le buffer doit �tre consid�r� comme vide ou plein � sa cr�ation. </param>
        /// <remarks> Si ownsArray vaut false, le tableau est copi� dans le buffer en une op�ration rapide qui ne met pas en jeu de verrou. </remarks>
        public ExclusiveCircularQueue( T[] array, bool ownsArray, bool empty ) : base( array, ownsArray, empty )
		{
            Initialize();
        }
		#endregion
		#region Impl�mentation
        private void Initialize()
        {
            canRead = new PredicateCondition( delegate() { return available != 0; }, syncExcl );
            canWrite = new PredicateCondition( delegate() { return capacity != 0; }, syncExcl );
        }
        /// <summary>
        /// Ajoute une valeur � la fin du buffer circulaire.
        /// </summary>
        /// <param name="value"> Valeur � ajouter. </param>
        public override void Enqueue( T value )
        {
			lock( syncExcl )
			{
				//attend qu'il y ait de la place dans le buffer
                canWrite.Wait();

                --capacity;
                EnqueueCore( value );
				++available;

				//avertir les threads attendant pour la lecture
                canRead.SignalAll();                
			}
		}
        /// <summary>
        /// Retire la valeur au d�but du buffer circulaire.
        /// </summary>
        /// <returns> Valeur retir�e. </returns>
        public override T Dequeue()
        {
			T temp;

			lock( syncExcl )
			{
				//attend qu'il y ait des valeurs � lire
                canRead.Wait();

                --available;
                temp = DequeueCore();
				++capacity;

				//avertir les threads attendant pour l'�criture
                canWrite.SignalAll();                
				return temp;
			}
		}
        /// <summary>
        /// Lit la valeur au d�but du buffer circulaire sans la retirer.
        /// </summary>
        /// <returns> Valeur lue. </returns>
        public override T Peek()
        {
            lock( syncExcl )
            {
                //attend qu'il y ait des blocs � lire
                canRead.Wait();

                //r�cup�re le bloc depuis le buffer
                return buffer[ readPosition ];
            }
        }
        /// <summary>
        /// Essaie de retirer la valeur au d�but du buffer circulaire sans attendre si aucune valeur n'est pr�sente dans le buffer.
        /// </summary>
        /// <param name="value"> Valeur retir�e en cas de succ�s. </param>
        /// <returns> true si une valeur a �t� retir�e, false sinon. </returns>
        public override bool TryDequeue( out T value )
        {
            if( Monitor.TryEnter( syncExcl ) )
            {
                try
                {
                    //regarde s'il y a une valeur � lire
                    if( available > 0 )
                    {
                        value = Dequeue();
                        return true;
                    }
                    else
                    {
                        value = default( T );
                        return false;
                    }
                }
                finally
                {
                    Monitor.Exit( syncExcl );
                }
            }
            else
            {
                value = default( T );
                return false;
            }
        }
		#endregion
	}
}