// Memory.h

#ifndef _MEMORY_SSL_H_INCLUDED_
#define _MEMORY_SSL_H_INCLUDED_


//#include <vector>

template <class Tnode> //universal memory node system
class MemorySlot
{
public:
    MemorySlot(int nodes);
    ~MemorySlot();

    Tnode * GetNode();
	Tnode * GetInactiveNode();
    void    ReleaseNode(Tnode *node);
	void	ReleaseDuplicateNodes(Tnode *node);
	bool    ContainsNode(Tnode *query);

private:
    long    m_Index;
    long    m_Nodes;
    Tnode * m_NodeSpace;
    bool    m_Resizable;
};


//Code for Reusable Memory slot template - needs to be here so auto-constructors in ObjectManager have visibility
template<class Tnode> 
MemorySlot<Tnode>::MemorySlot(int nodes)
{
	m_NodeSpace = new Tnode[nodes];
    if (m_NodeSpace)
    {
        m_Index = 0;
        m_Nodes = nodes;
    }
    else
    {
		LogMessage("FATAL ERROR: Unable to initialise memory.\n");
	}
}

template<class Tnode> 
Tnode * MemorySlot<Tnode>::GetNode()
{
	Tnode *slot;
    long start_slot = m_Index;

    if (!m_NodeSpace) //memory not valid
    {
        return (0);
    }
    
	slot = &m_NodeSpace[m_Index];

	m_Index++;

	if (m_Index == m_Nodes)
	{
        m_Index = 0;
	}

	return slot;
}

template<class Tnode> 
Tnode * MemorySlot<Tnode>::GetInactiveNode()
{
	Tnode *slot;
	long spincount = 0;

    if (!m_NodeSpace) //memory not valid
    {
        return (0);
    }

	while (m_NodeSpace[m_Index].GameID() != 0)
	{
		m_Index++;
		if (m_Index==m_Nodes)
		{
			m_Index = 0;
            spincount++;
            if (spincount > 2) return (0);
		}
	}
    
	slot = &m_NodeSpace[m_Index];

	m_Index++;

	if (m_Index == m_Nodes)
	{
        m_Index = 0;
	}

	return slot;
}

template<class Tnode>
void MemorySlot<Tnode>::ReleaseNode(Tnode *node)
{
    //finished with this tnode, mark as available
    node->SetActive(false);
}

template<class Tnode>
MemorySlot<Tnode>::~MemorySlot()
{
    delete [] m_NodeSpace;
}

template<class Tnode>
void MemorySlot<Tnode>::ReleaseDuplicateNodes(Tnode *node)
{
    //finished with this tnode, mark as available
	long i;
	for (i = 0; i < m_Nodes; i++)
	{
		if (&m_NodeSpace[i] != node && m_NodeSpace[i].GameID() == node->GameID())
		{
			m_NodeSpace[i].SetGameID(0);
		}
	}
}

template<class Tnode>
bool MemorySlot<Tnode>::ContainsNode(Tnode *query)
{
	return query >= m_NodeSpace && query < m_NodeSpace+m_Nodes;
}

#endif