; Searching a string with REPNE SCASB: find the position of the first ':' character.
include 'emu8086.inc'
#make_COM#
org 100h

    cld
    push ds
    pop es                  ; SCASB compares AL with ES:DI
    mov di, offset text
    mov cx, text_len
    mov al, ':'
    repne scasb             ; repeat while not equal and CX != 0
    jne not_found
    mov ax, di
    sub ax, offset text     ; DI stops one past the match: position is 1-based
    PRINT 'found at position '
    call PRINT_NUM_UNS
    ret
not_found:
    PRINT 'not found'
    ret

text     db 'key:value'
text_len equ $ - text

DEFINE_PRINT_NUM_UNS
END
